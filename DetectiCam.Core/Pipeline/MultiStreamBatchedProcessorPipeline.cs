using DetectiCam.Core.Common;
using DetectiCam.Core.Detection;
using DetectiCam.Core.Pipeline;
using DetectiCam.Core.ResultProcessor;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DetectiCam.Core.VideoCapturing
{
    /// <summary> A frame grabber. </summary>
    /// <typeparam name="TOutput"> Type of the analysis result. This is the type that
    ///     the AnalysisFunction will return, when it calls some API on a video frame. </typeparam>
    public class MultiStreamBatchedProcessorPipeline : ConfigurableService<MultiStreamBatchedProcessorPipeline, VideoStreamsOptions>, IDisposable
    {
        private readonly List<IAsyncSingleResultProcessor> _resultProcessors;

        private readonly List<VideoStreamGrabber> _streams = new List<VideoStreamGrabber>();
        private readonly VideoStreamsOptions _streamsConfig;

        private readonly IBatchedDnnDetector _detector;
        private readonly HeartbeatHealthCheck<MultiStreamBatchedProcessorPipeline> _healthCheck;

        private TimeSpan _analysisInterval = TimeSpan.FromSeconds(1);
        private PeriodicTrigger? _trigger;

        public MultiStreamBatchedProcessorPipeline([DisallowNull] ILogger<MultiStreamBatchedProcessorPipeline> logger,
                                              IOptions<VideoStreamsOptions> options,
                                              HeartbeatHealthCheck<MultiStreamBatchedProcessorPipeline> healthCheck,
                                              IBatchedDnnDetector detector,
                                              IEnumerable<IAsyncSingleResultProcessor> resultProcessors) :
            base(logger, options)
        {
            if (resultProcessors is null) throw new ArgumentNullException(nameof(resultProcessors));
            if (detector is null) throw new ArgumentNullException(nameof(detector));
            if (healthCheck is null) throw new ArgumentNullException(nameof(healthCheck));

            _detector = detector;
            _resultProcessors = new List<IAsyncSingleResultProcessor>(resultProcessors);
            _healthCheck = healthCheck;

            _streamsConfig = Options;

            Logger.LogInformation("Loaded configuration for {numberOfStreams} streams:{streamIds}",
                _streamsConfig.Count,
                String.Join(",", _streamsConfig.Select(s => s.Id)));
        }

        public void TriggerAnalysisOnInterval(TimeSpan interval)
        {
            _analysisInterval = interval;
        }

        private static Channel<VideoFrame> CreateCapturingChannel() =>
            Channel.CreateBounded<VideoFrame>(
                new BoundedChannelOptions(5)
                {
                    AllowSynchronousContinuations = true,
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = true
                });

        private static Channel<IList<VideoFrame>> CreateMultiFrameChannel() =>
            Channel.CreateBounded<IList<VideoFrame>>(
                new BoundedChannelOptions(1)
                {
                    AllowSynchronousContinuations = true,
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = true
                });

        private static Channel<IList<VideoFrame>> CreateOutputChannel() =>
            Channel.CreateUnbounded<IList<VideoFrame>>(
            new UnboundedChannelOptions()
            {
                AllowSynchronousContinuations = true,
                SingleReader = true,
                SingleWriter = true
            });

        private readonly List<Channel<VideoFrame>> _capturingChannels = new List<Channel<VideoFrame>>();

        public async Task StartCapturingAllStreamsAsync(CancellationToken cancellationToken)
        {
            Logger.LogInformation("Start Capturing All Streams");
            List<Task> streamsStartedTasks = new List<Task>();

            foreach (var stream in _streams)
            {
                Logger.LogInformation("Start Capturing: {streamId}", stream.Info.Id);
                var captureStartedTask = stream.StartCapturing(cancellationToken);
                Logger.LogInformation("Capturing Started: {streamId}", stream.Info.Id);
                streamsStartedTasks.Add(captureStartedTask);
            }

            await Task.WhenAll(streamsStartedTasks).ConfigureAwait(false);
            streamsStartedTasks.Clear();
        }

        private void CreateCapturingChannel(VideoStreamInfo streamInfo)
        {
            Logger.LogDebug("CreateCapturingChannel: {streamId}", streamInfo.Id);

            var newChannel = CreateCapturingChannel();
            _capturingChannels.Add(newChannel);

            VideoStreamGrabber vs = new VideoStreamGrabber(Logger, streamInfo, newChannel);

            _streams.Add(vs);

        }

        private void CreateCapturingChannels()
        {
            Logger.LogDebug("CreateCapturingChannels");
            foreach (var si in _streamsConfig)
            {
                CreateCapturingChannel(si);
            }
        }

        private SyncedMultiChannelMerger<VideoFrame>? _merger;
        private DnnDetectorChannelTransformer? _analyzer;
        private AnalyzedVideoFrameChannelConsumer? _resultPublisher;

        public async Task StartProcessingAll(CancellationToken cancellationToken)
        {
            Logger.LogInformation("Create processing pipeline");
            CreateCapturingChannels();
            var analysisChannel = CreateMultiFrameChannel();
            var outputChannel = CreateOutputChannel();

            var inputReaders = _capturingChannels.Select(c => c.Reader).ToList();

            _merger = new SyncedMultiChannelMerger<VideoFrame>(
                inputReaders, analysisChannel.Writer, Logger);
            var mergerTask = _merger.ExecuteProcessingAsync(cancellationToken);

            _analyzer = new DnnDetectorChannelTransformer(_detector,
                analysisChannel.Reader, outputChannel.Writer, _healthCheck, Logger);
            var analyzerTask = _analyzer.ExecuteProcessingAsync(cancellationToken);

            _resultPublisher = new AnalyzedVideoFrameChannelConsumer(
                outputChannel.Reader, _resultProcessors, Logger);
            var resultPublisherTask = _resultPublisher.ExecuteProcessingAsync(cancellationToken);

            Logger.LogInformation("Start processing pipeline");
            await StartCapturingAllStreamsAsync(cancellationToken).ConfigureAwait(false);

            //Only start the trigger when we know that all capturing streams have started.
            _trigger = new PeriodicTrigger(Logger, _streams);
            _trigger.Start(TimeSpan.FromSeconds(_streams.Count), _analysisInterval);

            await Task.WhenAll(mergerTask, analyzerTask, resultPublisherTask).ConfigureAwait(false);
        }

        /// <summary> Stops capturing and processing video frames. </summary>
        /// <returns> A Task. </returns>
        public async Task StopProcessingAsync()
        {
            Logger.LogInformation("Stopping capturing tasks");
            foreach (VideoStreamGrabber vs in _streams)
            {
                await vs.StopProcessingAsync().ConfigureAwait(false);
                vs.Dispose();
            }
            _streams.Clear();


            Logger.LogInformation("Stopping merger");
            if (_merger != null)
            {
                await _merger.StopProcessingAsync().ConfigureAwait(false);
                _merger.Dispose();
                _merger = null;
            }

            Logger.LogInformation("Stopping analyzer");
            if (_analyzer != null)
            {
                await _analyzer.StopProcessingAsync().ConfigureAwait(false);
                _analyzer.Dispose();
                _analyzer = null;
            }

            Logger.LogInformation("Stopping result publisher");
            if (_resultPublisher != null)
            {
                await _resultPublisher.StopProcessingAsync(default).ConfigureAwait(false);
                _resultPublisher.Dispose();
                _resultPublisher = null;
            }
        }


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            _trigger?.Dispose();
            foreach (VideoStreamGrabber vs in _streams)
            {
                vs?.Dispose();
            }
            _streams.Clear();
            _merger?.Dispose();
            _analyzer?.Dispose();
            _resultPublisher?.Dispose();
        }
    }
}
