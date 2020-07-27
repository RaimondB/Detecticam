#nullable enable
// Uncomment this to enable the LogMessage function, which can with debugging timing issues.
#define TRACE_GRABBER

using DetectiCam.Core.Detection;
using DetectiCam.Core.Pipeline;
using DetectiCam.Core.ResultProcessor;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DetectiCam.Core.VideoCapturing
{
    /// <summary> A frame grabber. </summary>
    /// <typeparam name="TOutput"> Type of the analysis result. This is the type that
    ///     the AnalysisFunction will return, when it calls some API on a video frame. </typeparam>
    public class MultiStreamBatchedProcessorPipeline : IDisposable
    {
        private readonly List<IAsyncSingleResultProcessor> _resultProcessors;

        private readonly List<VideoStreamGrabber> _streams = new List<VideoStreamGrabber>();
        private readonly VideoStreamsConfigCollection _streamsConfig;

        private bool disposedValue = false;
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;
        private readonly IBatchedDnnDetector _detector;

        private TimeSpan _analysisInterval = TimeSpan.FromSeconds(3);

        public MultiStreamBatchedProcessorPipeline([DisallowNull] ILogger<MultiStreamBatchedProcessorPipeline> logger,
                                              [DisallowNull] IConfiguration configuration,
                                              IBatchedDnnDetector detector,
                                              IEnumerable<IAsyncSingleResultProcessor> resultProcessors)
        {
            if (logger is null) throw new ArgumentNullException(nameof(logger));
            if (configuration is null) throw new ArgumentNullException(nameof(configuration));
            if (resultProcessors is null) throw new ArgumentNullException(nameof(resultProcessors));
            if (detector is null) throw new ArgumentNullException(nameof(detector));

            _logger = logger;
            _configuration = configuration;
            _detector = detector;
            _resultProcessors = new List<IAsyncSingleResultProcessor>(resultProcessors);

            _streamsConfig = _configuration.GetSection(VideoStreamsConfigCollection.VideoStreamsConfigKey).Get<VideoStreamsConfigCollection>();

            _logger.LogInformation("Loaded configuration for {numberOfStreams} streams:{streamIds}", 
                _streamsConfig.Count,
                String.Join(",", _streamsConfig.Select(s => s.Id)));
        }

        public void TriggerAnalysisOnInterval(TimeSpan interval)
        {
            _analysisInterval = interval;
        }

        private static Channel<VideoFrame> CreateCapturingChannel() =>
            Channel.CreateBounded<VideoFrame>(
                new BoundedChannelOptions(2)
                {
                    AllowSynchronousContinuations = false,
                    FullMode = BoundedChannelFullMode.DropNewest,
                    SingleReader = true,
                    SingleWriter = true
                });

        private static Channel<IList<VideoFrame>> CreateMultiFrameChannel() =>
            Channel.CreateBounded<IList<VideoFrame>>(
            new BoundedChannelOptions(2)
            {
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.DropNewest,
                SingleReader = true,
                SingleWriter = true
            });

        private static Channel<AnalysisResult> CreateOutputChannel() =>
            Channel.CreateUnbounded<AnalysisResult>(
            new UnboundedChannelOptions()
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = true
            });

        private readonly List<Channel<VideoFrame>> _capturingChannels = new List<Channel<VideoFrame>>();

        public void StartCapturingAllStreamsAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Start Capturing All Streams");
            foreach (var stream in _streams)
            {
                _logger.LogInformation("Start Capturing: {streamId}", stream.Info.Id);
                stream.StartCapturing(_analysisInterval, cancellationToken);
            }
        }

        private void CreateCapturingChannel(VideoStreamInfo streamInfo)
        {
            _logger.LogInformation("CreateCapturingChannel: {streamId}", streamInfo.Id);

            var newChannel = CreateCapturingChannel();
            _capturingChannels.Add(newChannel);

            VideoStreamGrabber vs = new VideoStreamGrabber(_logger, streamInfo, newChannel);

            _streams.Add(vs);

        }

        private void CreateCapturingChannels()
        {
            _logger.LogDebug("CreateCapturingChannels");
            foreach (var si in _streamsConfig)
            {
                CreateCapturingChannel(si);
            }
        }

        private MultiChannelMerger<VideoFrame>? _merger;
        private DnnDetectorChannelTransformer? _analyzer;
        private AnalysisResultsChannelConsumer? _resultPublisher;

        public Task StartProcessingAll(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Create processing pipeline");
            CreateCapturingChannels();
            var analysisChannel = CreateMultiFrameChannel();
            var outputChannel = CreateOutputChannel();

            var inputReaders = _capturingChannels.Select(c => c.Reader).ToList();

            _merger = new MultiChannelMerger<VideoFrame>(
                inputReaders, analysisChannel.Writer, _logger);
            var mergerTask = _merger.ExecuteProcessingAsync(cancellationToken);

            _analyzer = new DnnDetectorChannelTransformer(_detector,
                analysisChannel.Reader, outputChannel.Writer, _logger);
            var analyzerTask = _analyzer.ExecuteProcessingAsync(cancellationToken);

            _resultPublisher = new AnalysisResultsChannelConsumer(
                outputChannel.Reader, _resultProcessors, _logger);
            var resultPublisherTask = _resultPublisher.ExecuteProcessingAsync(cancellationToken);

            _logger.LogInformation("Start processing pipeline");
            StartCapturingAllStreamsAsync(cancellationToken);

            return Task.WhenAll(mergerTask, analyzerTask, resultPublisherTask);
        }

        /// <summary> Stops capturing and processing video frames. </summary>
        /// <returns> A Task. </returns>
        public async Task StopProcessingAsync()
        {
            _logger.LogInformation("Stopping capturing tasks");
            foreach (VideoStreamGrabber vs in _streams)
            {
                await vs.StopProcessingAsync().ConfigureAwait(false);
                vs.Dispose();
            }
            _streams.Clear();


            _logger.LogInformation("Stopping merger");
            if (_merger != null)
            {
                await _merger.StopProcessingAsync().ConfigureAwait(false);
                _merger.Dispose();
                _merger = null;
            }

            _logger.LogInformation("Stopping analyzer");
            if (_analyzer != null)
            {
                await _analyzer.StopProcessingAsync().ConfigureAwait(false);
                _analyzer.Dispose();
                _analyzer = null;
            }

            _logger.LogInformation("Stopping result publisher");
            if (_resultPublisher != null)
            {
                await _resultPublisher.StopProcessingAsync(default).ConfigureAwait(false);
                _resultPublisher.Dispose();
                _resultPublisher = null;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    foreach (VideoStreamGrabber vs in _streams)
                    {
                        vs?.Dispose();
                    }
                    _streams.Clear();
                    _merger?.Dispose();
                    _merger = null;
                    _analyzer?.Dispose();
                    _analyzer = null;
                    _resultPublisher?.Dispose();
                    _resultPublisher = null;
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
