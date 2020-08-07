//#define TRACE_GRABBER
#nullable enable

using DetectiCam.Core.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using OpenCvSharp;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DetectiCam.Core.VideoCapturing
{

    public class VideoStreamGrabber : IDisposable, ITimestampTrigger
    {
        public string Path { get; }

        private double _fps;
        public double Fps => _fps;

        public string StreamName { get; }

        public bool IsContinuous { get; }

        public RotateFlags? RotateFlags { get; }

        private Task? _executionTask;
        private readonly ILogger _logger;
        public VideoStreamInfo Info { get; }
        public Channel<VideoFrame> OutputChannel { get; }

        private readonly ChannelWriter<VideoFrame> _outputWriter;

        private readonly CancellationTokenSource _internalCts;

        private readonly Channel<(Mat Frame, DateTime Timestamp, int FrameCount)> _frameBufferChannel = 
            Channel.CreateBounded <(Mat Frame, DateTime Timestamp, int FrameCount)>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                AllowSynchronousContinuations = true,
                SingleReader = true,
                SingleWriter = true
            });
        private readonly ChannelReader<(Mat Frame, DateTime Timestamp, int FrameCount)> _frameBufferReader;
        private readonly ChannelWriter<(Mat Frame, DateTime Timestamp, int FrameCount)> _frameBufferWriter;

        private readonly Mat _image1 = new Mat();
        private readonly Mat _image2 = new Mat();

        private enum GrabResult
        {
            Succeeded = 0,
            FailedRetry = 1,
            FailAbort = 2,
            FailRestart = 3
        }


        public VideoStreamGrabber(ILogger logger, VideoStreamInfo streamInfo, Channel<VideoFrame> outputChannel)
        {
            if (logger is null) throw new ArgumentNullException(nameof(logger));
            if (streamInfo is null) throw new ArgumentNullException(nameof(streamInfo));
            if (outputChannel is null) throw new ArgumentNullException(nameof(outputChannel));

            Info = streamInfo;
            Path = streamInfo.Path;
            _fps = streamInfo.Fps;
            StreamName = streamInfo.Id;
            IsContinuous = streamInfo.IsContinuous;
            RotateFlags = streamInfo.RotateFlags;
            OutputChannel = outputChannel;
            _outputWriter = outputChannel.Writer;

            _frameBufferReader = _frameBufferChannel.Reader;
            _frameBufferWriter = _frameBufferChannel.Writer;

            _internalCts = new CancellationTokenSource();

            _logger = logger;
        }



        [Conditional("TRACE_GRABBER")]
        private void LogTrace(string message, params object[] args)
        {
            _logger.LogTrace(message, args);
        }

        public VideoCapture InitCapture()
        {
            var videoCapture = new VideoCapture(Path);

            if (Fps == 0)
            {
                var rFpds = videoCapture.Fps;

                if (rFpds > 0 && rFpds < 60)
                {
                    _logger.LogInformation($"Init Fps from stream:{this.Info.Id} at {rFpds}");
                    _fps = rFpds;

                }
                else
                {
                    _logger.LogInformation($"Fps {rFpds} invalid Init Fps from stream:{this.Info.Id}. Fallback to 30 fps");
                    _fps = 30;
                }
            }
            else
            {
                _logger.LogInformation($"Init Forced Fps from stream:{this.Info.Id} at {Fps}");
            }

            return videoCapture;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public Task StartCapturing(CancellationToken cancellationToken)
        {
            //Create a completion source to signal the moment that the capturing has been started (or has failed).
            var capstureStartedTcs = new TaskCompletionSource<Object>();

            _executionTask = Task.Run(() =>
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                    _internalCts.Token, cancellationToken);
                var linkedToken = cts.Token;

                try
                {
                    while (!linkedToken.IsCancellationRequested)
                    {
                        try
                        {
                            using var reader = this.InitCapture();

                            int delayMs = (int)(500.0 / this.Fps);
                            int errorCount = 0;
                            int frameCount = 0;
                            bool restart = false;

                            while (!linkedToken.IsCancellationRequested && !restart )
                            {
                                var result = GrabFrame(reader, frameCount++, ref errorCount);

                                switch (result)
                                {
                                    case GrabResult.Succeeded:
                                        if(frameCount ==1) capstureStartedTcs.SetResult(true);
                                        Thread.Sleep(delayMs);
                                        break;
                                    case GrabResult.FailAbort:
                                        if (frameCount == 1) capstureStartedTcs.SetException(new Exception($"Capturing failed for: {this.Info.Id}"));
                                        _internalCts.Cancel();
                                        break;
                                    case GrabResult.FailedRetry:
                                        break;
                                    case GrabResult.FailRestart:
                                        restart = true;
                                        break;
                                }
                            }
                        }
#pragma warning disable CA1031 // Do not catch general exception types
                        catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
                        {
                            capstureStartedTcs.SetException(ex);
                            _logger.LogError(ex, "Exception in processes videostream {name}, restarting", this.StreamName);
                        }
                    }
                }
                finally
                {
                    // We reach this point by breaking out of the while loop. So we must be stopping.
                    _logger.LogInformation("Capture has stopped for {streamId}", this.Info.Id);
                    _outputWriter.TryComplete();
                }
            }, cancellationToken);

            return capstureStartedTcs.Task;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining| MethodImplOptions.AggressiveOptimization)]
        private GrabResult GrabFrame(VideoCapture reader, int frameCount, ref int errorCount)
        {
            Mat imageBuffer = (frameCount % 2 == 0) ? _image1 : _image2;

            var startTime = DateTime.Now;
            bool success = reader.Read(imageBuffer);

#if(TRACE_GRABBER)
            var endTime = DateTime.Now;
            LogTrace("Producer: frame-grab took {0} ms", (endTime - startTime).Milliseconds);
#endif

            if (success && !imageBuffer.Empty())
            {

                var frameContext = (Frame: imageBuffer, Timestamp: startTime, FrameCount: frameCount);
                _frameBufferWriter.TryWrite(frameContext);

                return GrabResult.Succeeded;
            }
            else
            {
                // If we've reached the end of the video, stop here.
                if (IsContinuous)
                {
                    errorCount++;
                    // If failed on live camera, try again.
                    _logger.LogWarning("Producer: null frame from live camera, continue! ({errorCount} errors)", errorCount);

                    if (errorCount < 5)
                    {
                        _logger.LogWarning("Error in capture, retry");
                        return GrabResult.FailedRetry;
                    }
                    else
                    {
                        _logger.LogWarning("Errorcount exceeded, restarting videocapture");
                        return GrabResult.FailRestart;
                    }
                }
                else
                {
                    _logger.LogWarning("Producer: null frame from video file, stop!");
                    return GrabResult.FailAbort;
                }
            }
        }

        private Mat PreprocessImage(Mat imageBuffer)
        {
            Mat publishedImage;
            if (RotateFlags.HasValue)
            {
                Mat rotImage = new Mat();
                Cv2.Rotate(imageBuffer, rotImage, RotateFlags.Value);

                publishedImage = rotImage;
            }
            else
            {
                Mat cloneImage = new Mat();
                Cv2.CopyTo(imageBuffer, cloneImage);

                publishedImage = cloneImage;
            }

            return publishedImage;
        }

        public async Task StopProcessingAsync()
        {
            LogTrace("Producer: stopping, destroy reader and timer");
            _internalCts.Cancel();
            if (_executionTask != null)
            {
                await _executionTask.ConfigureAwait(false);
                _executionTask = null;
            }
        }

        public void ExecuteTrigger(DateTime timestamp, int triggerId)
        {
            if (_frameBufferReader.TryRead(out var frameContext))
            {
                Mat imageToPublish = PreprocessImage(frameContext.Frame);

                var ctx = new VideoFrameContext(frameContext.Timestamp, frameContext.FrameCount, this.Info);

#pragma warning disable CA2000 // Dispose objects before losing scope
                var videoFrame = new VideoFrame(imageToPublish, ctx)
                {
                    TriggerId = triggerId
                };
                //object should not be disposed here, since it is written to a channel for further processing.
#pragma warning restore CA2000 // Dispose objects before losing scope

                if (_outputWriter.TryWrite(videoFrame))
                {
                    _logger.LogDebug("Producer: Published frame {timestamp}; {streamId}; of trigger: {triggerId}",
                        videoFrame.Metadata.Timestamp, this.Info.Id, triggerId);
                }
                else
                {
                    _logger.LogWarning("Producer: Could not publish frame {timestamp}; {streamId}; of trigger: {triggerId}",
                        videoFrame.Metadata.Timestamp, this.Info.Id, triggerId);
                    videoFrame.Dispose();
                }
            }
            else
            {
                _logger.LogWarning("Producer: No frame available to publish");
            }
        }

        public void Dispose()
        {
            _internalCts.Cancel();
            StopProcessingAsync()?.Wait(2000);

            _internalCts?.Dispose();

            _image1?.Dispose();
            _image2?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
