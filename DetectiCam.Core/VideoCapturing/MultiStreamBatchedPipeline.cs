#nullable enable
// Uncomment this to enable the LogMessage function, which can with debugging timing issues.
#define TRACE_GRABBER

using DetectiCam.Core.Detection;
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
    public class MultiStreamBatchedPipeline : IDisposable
    {
        #region Properties

        /// <summary> Gets or sets the analysis function. The function can be any asynchronous
        ///     operation that accepts a <see cref="VideoFrame"/> and returns a
        ///     <see cref="Task{AnalysisResultType}"/>. </summary>
        /// <value> The analysis function. </value>
        public Func<IList<VideoFrame>, CancellationToken, Task<IList<DnnDetectedObject[]>>>? AnalysisFunction { get; set; } = null;

        /// <summary> Gets or sets the analysis timeout. When executing the
        ///     <see cref="AnalysisFunction"/> on a video frame, if the call doesn't return a
        ///     result within this time, it is abandoned and no result is returned for that
        ///     frame. </summary>
        /// <value> The analysis timeout. </value>
        public TimeSpan AnalysisTimeout { get; set; } = TimeSpan.FromMilliseconds(2500);

        public bool IsRunning { get { return _mergeTask != null; } }

        public Channel<AnalysisResult> OutputChannel { get; }

        private readonly List<IAsyncSingleResultProcessor> _resultProcessors;

        #endregion Properties

        #region Fields

        private readonly List<VideoStreamGrabber> _streams = new List<VideoStreamGrabber>();
        private readonly VideoStreamsConfigCollection _streamsConfig;

        private bool _stopping = false;
        private Task? _consumerTask = null;
        private Task? _mergeTask = null;

        private bool disposedValue = false;
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;

        #endregion Fields

        #region Methods


        public MultiStreamBatchedPipeline([DisallowNull] ILogger<MultiStreamBatchedPipeline> logger,
                                              [DisallowNull] IConfiguration configuration,
                                              IEnumerable<IAsyncSingleResultProcessor> resultProcessors)
        {
            if (logger is null) throw new ArgumentNullException(nameof(logger));
            if (configuration is null) throw new ArgumentNullException(nameof(configuration));
            if (resultProcessors is null) throw new ArgumentNullException(nameof(resultProcessors));

            _logger = logger;
            _configuration = configuration;
            _resultProcessors = new List<IAsyncSingleResultProcessor>(resultProcessors);

            _streamsConfig = _configuration.GetSection(VideoStreamsConfigCollection.VideoStreamsConfigKey).Get<VideoStreamsConfigCollection>();

            _logger.LogInformation("Loaded configuration for {numberOfStreams} streams:{streamIds}", 
                _streamsConfig.Count,
                String.Join(",", _streamsConfig.Select(s => s.Id)));

            OutputChannel = CreateOutputChannel();
        }

        protected async Task<AnalysisResult?> DoAnalyzeFrames(IList<VideoFrame> frames, CancellationToken cancellationToken)
        {
            using CancellationTokenSource timeoutSource = new CancellationTokenSource();

            // Make a local reference to the function, just in case someone sets
            // AnalysisFunction = null before we can call it.
            var fcn = AnalysisFunction;
            if (fcn != null)
            {
                var output = new AnalysisResult(frames);
                
                try
                {
                    var task = fcn(frames, cancellationToken);
                    _logger.LogDebug("DoAnalysis: started task {taskId}", task.Id);
                    
                    //if (task == await Task.WhenAny(task, Task.Delay(AnalysisTimeout, source.Token)))
                    if (task == await Task.WhenAny(task, Task.Delay(AnalysisTimeout, timeoutSource.Token)).ConfigureAwait(false))
                    {
                        output.Analysis = await task.ConfigureAwait(false);
                        timeoutSource.Cancel();
                    }
                    else
                    {
                        _logger.LogDebug("DoAnalysis: Timeout from task {taskId}", task.Id);
                        output.TimedOut = true;
                    }
                    _logger.LogDebug("DoAnalysis: returned from task {0}", task.Id);
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch (Exception ae)
#pragma warning restore CA1031 // Do not catch general exception types
                {
                    output.Exception = ae;
                    _logger.LogDebug("DoAnalysis: Exception from analysis task:{message}", ae.Message);
                }

                return output;
            }
            else
            {
                return null;
            }
        }

        private TimeSpan _analysisInterval;

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
            _logger.LogInformation("CreateCapturingChannels");
            foreach (var si in _streamsConfig)
            {
                CreateCapturingChannel(si);
            }
        }


        public Task MergeChannels<T>(
            IList<Channel<T>> inputChannels, Channel<IList<T>> outputChannel, CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                //var timeout = TimeSpan.FromMilliseconds(50);

                var readers = inputChannels.Select(c => c.Reader).ToArray();
                var writer = outputChannel.Writer;

                while (!_stopping && !cancellationToken.IsCancellationRequested)
                {
                    List<T> results = new List<T>();

                    for (var index = 0; index < readers.Length; index++)
                    {
                        try
                        {
                            var curReader = readers[index];
                            if (curReader != null)
                            {
                                var result = await readers[index].ReadAsync(cancellationToken).ConfigureAwait(false);
                                results.Add(result);
                            }
                        }
                        catch(ChannelClosedException)
                        {
                            _logger.LogWarning("Channel closed");
                            //readers[index] = null;
                        }
#pragma warning disable CA1031 // Do not catch general exception types
                        catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
                        {
                            _logger.LogError("Exception during channel merge:{exception}", ex);
                            //Just continue to next on errors or timeouts
                        }
                    }
                    if(!writer.TryWrite(results))
                    {
                        _logger.LogWarning("Could not write merged result!");
                    }
                }
            }, cancellationToken);
        }

        private Task CreateResultProcessorTask(CancellationToken cancellationToken)
        { 
            var resultWriterTask = Task.Run(async () =>
            {
                var resultReader = this.OutputChannel.Reader;

                await foreach(var result in resultReader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    await ProcessAnalysisResult(result).ConfigureAwait(false);
                }
            });

            return resultWriterTask;
        }

        private async Task ProcessAnalysisResult(AnalysisResult e)
        {
            if (e.TimedOut)
                _logger.LogWarning("Analysis function timed out.");
            else if (e.Exception != null)
                _logger.LogError(e.Exception, "Analysis function threw an exception");
            else
            {
                for (int index = 0; index < e.Frames.Count; index++)
                {
                    var frame = e.Frames[index];
                    var analysis = e.Analysis[index];

                    using Mat inputImage = frame.Image;

                    _logger.LogInformation($"New result received for frame acquired at {frame.Metadata.Timestamp}. Sending to result processors");

                    var resultTasks = new List<Task>();  
                    foreach (var processor in _resultProcessors)
                    {
                        resultTasks.Add(processor.ProcessResultAsync(frame, analysis));
                    }
                    await Task.WhenAll(resultTasks).ConfigureAwait(false);
                }
            }
        }


        public Task StartProcessingAll(CancellationToken cancellationToken)
        {
            CreateCapturingChannels();
            var analysisChannel = CreateMultiFrameChannel();
            _mergeTask = MergeChannels(_capturingChannels, analysisChannel, cancellationToken);

            _logger.LogInformation("Starting Consumer Task");
            _consumerTask = Task.Run(async () =>
            {
                var reader = analysisChannel.Reader;
                var writer = OutputChannel.Writer;

                while (!_stopping && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Consumer: waiting for next result to arrive");

                    try
                    {
                        var vframes = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                        var startTime = DateTime.Now;

                        var result = await DoAnalyzeFrames(vframes, cancellationToken).ConfigureAwait(false);

                        if (result != null)
                        {
                            if (result.Analysis != null)
                            {
                                _logger.LogInformation("Consumer: analysis took {analysisDuration} ms", (DateTime.Now - startTime).TotalMilliseconds);

                                writer.TryWrite(result);
                            }
                            if (result.TimedOut)
                            {
                                _logger.LogWarning("Consumer: analysis timed out!");
                            }
                            if (result.Exception != null)
                            {
                                _logger.LogError(result.Exception, "Exception occured in analysis");
                            }
                        }
                        else
                        {
                            _logger.LogError("Consumer: analysis returned null!");
                        }
                    }
#pragma warning disable CA1031 // Do not catch general exception types
                    catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
                    {
                        _logger.LogError(ex, "Exception in consumertask");
                        //try to continue always
                    }
                }

                _logger.LogInformation("Consumer: stopping");
            });

            var resultWriterTask = CreateResultProcessorTask(cancellationToken);

            StartCapturingAllStreamsAsync(cancellationToken);

            return resultWriterTask;
        }

        /// <summary> Stops capturing and processing video frames. </summary>
        /// <returns> A Task. </returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "Sync needed because of eventhandlers")]
        public async Task StopProcessingAsync()
        {
            _stopping = true;

            _logger.LogInformation("Stopping capturing tasks");
            foreach (VideoStreamGrabber vs in _streams)
            {
                await vs.StopProcessingAsync();
                vs.Dispose();
            }
            _streams.Clear();


            _logger.LogInformation("Stopping consumer task");
            if (_consumerTask != null)
            {
                await _consumerTask;
                _consumerTask = null;
            }

            _logger.LogInformation("Stopping merge task");
            if (_mergeTask != null)
            {
                await _mergeTask;
                _mergeTask = null;
            }

            _stopping = false;
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
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion Methods
    }
}
