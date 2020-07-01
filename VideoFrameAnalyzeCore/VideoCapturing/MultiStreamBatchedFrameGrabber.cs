#nullable enable
// Uncomment this to enable the LogMessage function, which can with debugging timing issues.
#define TRACE_GRABBER

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
using VideoFrameAnalyzeCore.VideoCapturing;
using VideoFrameAnalyzeStd.VideoCapturing;

namespace VideoFrameAnalyzer
{
    /// <summary> A frame grabber. </summary>
    /// <typeparam name="TAnalysisResultType"> Type of the analysis result. This is the type that
    ///     the AnalysisFunction will return, when it calls some API on a video frame. </typeparam>
    public class MultiStreamBatchedFrameGrabber<TAnalysisResultType> : IDisposable
    {
        #region Properties

        /// <summary> Gets or sets the analysis function. The function can be any asynchronous
        ///     operation that accepts a <see cref="VideoFrame"/> and returns a
        ///     <see cref="Task{AnalysisResultType}"/>. </summary>
        /// <value> The analysis function. </value>
        /// <example> This example shows how to provide an analysis function using a lambda expression.
        ///     <code>
        ///     var client = new FaceServiceClient("subscription key", "api root");
        ///     var grabber = new FrameGrabber();
        ///     grabber.AnalysisFunction = async (frame) =&gt; { return await client.RecognizeAsync(frame.Image.ToMemoryStream(".jpg")); };
        ///     </code></example>
        public Func<IList<VideoFrame>, Task<TAnalysisResultType>>? AnalysisFunction { get; set; } = null;

        /// <summary> Gets or sets the analysis timeout. When executing the
        ///     <see cref="AnalysisFunction"/> on a video frame, if the call doesn't return a
        ///     result within this time, it is abandoned and no result is returned for that
        ///     frame. </summary>
        /// <value> The analysis timeout. </value>
        public TimeSpan AnalysisTimeout { get; set; } = TimeSpan.FromMilliseconds(5000);

        public bool IsRunning { get { return _mergeTask != null; } }

        //public double FrameRate
        //{
        //    get { return _fps; }
        //    set
        //    {
        //        _fps = value;
        //        if (_timer != null)
        //        {
        //            _timer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(1.0 / _fps));
        //        }
        //    }
        //}

        public Channel<AnalysisResult<TAnalysisResultType>> OutputChannel { get; }

        #endregion Properties

        #region Fields

        private readonly List<VideoStream> _streams = new List<VideoStream>();
        private readonly VideoStreamsConfigCollection _streamsConfig;

        private bool _stopping = false;
        private Task? _consumerTask = null;
        private Task? _mergeTask = null;

        private bool disposedValue = false;
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;

        #endregion Fields

        #region Methods


        public MultiStreamBatchedFrameGrabber([DisallowNull] ILogger<MultiStreamBatchedFrameGrabber<TAnalysisResultType>> logger,
                                              [DisallowNull] IConfiguration configuration)
        {
            if (logger is null) throw new ArgumentNullException(nameof(logger));
            if (configuration is null) throw new ArgumentNullException(nameof(configuration));

            _logger = logger;
            _configuration = configuration;

            _streamsConfig = _configuration.GetSection("video-streams").Get<VideoStreamsConfigCollection>();

            _logger.LogInformation("Loaded configuration for {numberOfStreams} streams:{streamIds}", _streamsConfig.Count,
                String.Join(",", _streamsConfig.Select(s => s.Id)));

            OutputChannel = CreateOutputChannel();
        }

        /// <summary> (Only available in TRACE_GRABBER builds) logs a message. </summary>
        /// <param name="format"> Describes the format to use. </param>
        /// <param name="args">   Event information. </param>
        [Conditional("TRACE_GRABBER")]
        protected void LogMessage(string format, params object[] args)
        {
            _logger.LogInformation(String.Format(CultureInfo.InvariantCulture, format, args));
        }

        [Conditional("TRACE_GRABBER")]
        protected void LogDebug(string format, params object[] args)
        {
            _logger.LogDebug(String.Format(CultureInfo.InvariantCulture, format, args));
        }

        [Conditional("TRACE_GRABBER")]
        protected void LogWarning(string format, params object[] args)
        {
            _logger.LogDebug(String.Format(CultureInfo.InvariantCulture, format, args));
        }

        protected async Task<AnalysisResult<TAnalysisResultType>?> DoAnalyzeFrames(IList<VideoFrame> frames)
        {
            using CancellationTokenSource source = new CancellationTokenSource();
            
            // Make a local reference to the function, just in case someone sets
            // AnalysisFunction = null before we can call it.
            var fcn = AnalysisFunction;
            if (fcn != null)
            {
                var output = new AnalysisResult<TAnalysisResultType>(frames);
                var task = fcn(frames);
                LogDebug("DoAnalysis: started task {0}", task.Id);
                try
                {
                    if (task == await Task.WhenAny(task, Task.Delay(AnalysisTimeout, source.Token)).ConfigureAwait(false))
                    {
                        output.Analysis = await task.ConfigureAwait(false);
                        source.Cancel();
                    }
                    else
                    {
                        LogWarning("DoAnalysis: Timeout from task {0}", task.Id);
                        output.TimedOut = true;
                    }
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch (Exception ae)
#pragma warning restore CA1031 // Do not catch general exception types
                {
                    output.Exception = ae;
                    LogWarning("DoAnalysis: Exception from task {0}:{1}", task.Id, ae.Message);
                }

                LogDebug("DoAnalysis: returned from task {0}", task.Id);

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

        private static Channel<AnalysisResult<TAnalysisResultType>> CreateOutputChannel() =>
            Channel.CreateUnbounded<AnalysisResult<TAnalysisResultType>>(
            new UnboundedChannelOptions()
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = true
            });

        private readonly List<Channel<VideoFrame>> _capturingChannels = new List<Channel<VideoFrame>>();


        public void StartProcessingFileAsync(VideoStreamInfo streamInfo)
        {
            VideoStream vs = new VideoStream(_logger, streamInfo);

            _streams.Add(vs);

            var newChannel = CreateCapturingChannel();
            _capturingChannels.Add(newChannel);

            vs.StartProcessingAsync(newChannel, TimeSpan.FromSeconds(3));
        }

        public void StartCapturingAllStreamsAsync()
        {
            LogMessage("Start Capturing All Streams");
            foreach (var si in _streamsConfig)
            {
                _logger.LogInformation("Start Capturing: {streamId}", si.Id);
                StartProcessingFileAsync(si);
            }
        }

        public Task MergeChannels<T>(
            IList<Channel<T>> inputChannels, Channel<IList<T>> outputChannel, TimeSpan mergeDelay)
        {
            return Task.Run(async () =>
            {
                //var timeout = TimeSpan.FromMilliseconds(50);

                var readers = inputChannels.Select(c => c.Reader).ToArray();
                var writer = outputChannel.Writer;

                while (!_stopping)
                {
                    List<T> results = new List<T>();

                    for (var index = 0; index < readers.Length; index++)
                    {
                        try
                        {
                            var result = await readers[index].ReadAsync().ConfigureAwait(false);
                            results.Add(result);
                        }
#pragma warning disable CA1031 // Do not catch general exception types
                        catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
                        {
                            LogMessage($"Exception during channel merge:{ex}");
                            //Just continue to next on errors or timeouts
                        }
                    }
                    if(!writer.TryWrite(results))
                    {
                        LogWarning("Could not write merged result!");
                    }

                    //await Task.Delay(mergeDelay).ConfigureAwait(false);
                }
            });
        }

        /// <summary> Starts capturing and processing video frames. </summary>
        /// <param name="frameGrabDelay"> The frame grab delay. </param>
        /// <param name="timestampFn">    Function to generate the timestamp for each frame. This
        ///     function will get called once per frame. </param>
        public void StartProcessingAll()
        {
            var analysisChannel = CreateMultiFrameChannel();
            _mergeTask = MergeChannels(_capturingChannels, analysisChannel, _analysisInterval);

            LogMessage("Starting Consumer Task");
            _consumerTask = Task.Run(async () =>
            {
                var reader = analysisChannel.Reader;
                var writer = OutputChannel.Writer;

                while (!_stopping)
                {
                    LogMessage("Consumer: waiting for next result to arrive");

                    try
                    {
                        var vframes = await reader.ReadAsync().ConfigureAwait(false);

                        var startTime = DateTime.Now;

                        var result = await DoAnalyzeFrames(vframes).ConfigureAwait(false);

                        if (result != null)
                        {
                            LogMessage("Consumer: analysis took {0} ms", (DateTime.Now - startTime).TotalMilliseconds);

                            writer.TryWrite(result);
                            //await writer.WriteAsync(result).ConfigureAwait(false);
                        }
                        else
                        {
                            LogWarning("Consumer: analysis returned null!");
                        }
                    }
#pragma warning disable CA1031 // Do not catch general exception types
                    catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
                    {
                        LogMessage($"Exception in consumertask:{ex.Message}");
                        //try to continue always
                    }
                }

                LogMessage("Consumer: stopping");
            });

            StartCapturingAllStreamsAsync();
        }

        /// <summary> Stops capturing and processing video frames. </summary>
        /// <returns> A Task. </returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "Sync needed because of eventhandlers")]
        public async Task StopProcessingAsync()
        {
            _stopping = true;

            LogMessage("Stopping consumer task");
            if (_consumerTask != null)
            {
                await _consumerTask;
                _consumerTask = null;
            }

            LogMessage("Stopping merge task");
            if (_mergeTask != null)
            {
                await _mergeTask;
                _mergeTask = null;
            }

            LogMessage("Stopping capturing tasks");
            foreach (VideoStream vs in _streams)
            {
                await vs.StopProcessingAsync();
                vs.Dispose();
            }
            _streams.Clear();

            _stopping = false;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    //_frameGrabTimer?.Dispose();
                    //_timer?.Dispose();
                    //_timerMutex?.Dispose();
                    //_analysisTaskQueue?.Dispose();
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
