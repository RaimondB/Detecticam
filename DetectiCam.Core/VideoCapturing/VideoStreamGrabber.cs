#define TRACE_GRABBER
#nullable enable

using DetectiCam.Core.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using OpenCvSharp;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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

        private VideoCapture? _videoCapture;
        public VideoCapture? VideoCapture => _videoCapture;

        public string StreamName { get; }

        public bool IsContinuous { get; }

        public RotateFlags? RotateFlags { get; }

        private bool _stopping;
        private bool disposedValue;
        private Task? _executionTask;
        private readonly ILogger _logger;
        public VideoStreamInfo Info { get; }
        public Channel<VideoFrame> OutputChannel { get; }

        private ChannelWriter<VideoFrame> _outputWriter;


        private Channel<VideoFrame> _frameBufferChannel = Channel.CreateBounded<VideoFrame>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                AllowSynchronousContinuations = true,
                SingleReader = true,
                SingleWriter = true
            });
        private ChannelReader<VideoFrame> _frameBufferReader;
        private ChannelWriter<VideoFrame> _frameBufferWriter;

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


            _stopping = false;
            _logger = logger;
        }



        [Conditional("TRACE_GRABBER")]
        private void LogTrace(string format, params object[] args)
        {
            _logger.LogTrace(String.Format(CultureInfo.InvariantCulture, format, args));
        }

        public VideoCapture InitCapture()
        {
            _videoCapture = new VideoCapture(Path);

            if (Fps == 0)
            {
                var rFpds = _videoCapture.Fps;

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

            return _videoCapture;
        }

        public void StartCapturing(TimeSpan publicationInterval, CancellationToken cancellationToken)
        {
            _executionTask = Task.Run(() =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested && !_stopping)
                    {
                        try
                        {
                            StartCapture(publicationInterval, cancellationToken);
                        }
#pragma warning disable CA1031 // Do not catch general exception types
                        catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
                        {
                            _logger.LogError(ex, "Exception in processes videostream {name}, restarting", this.StreamName);
                        }
                    }
                }
                finally
                {
                    // We reach this point by breaking out of the while loop. So we must be stopping.
                    _logger.LogInformation($"Capture has stopped for {this.Info.Id}");
                    _outputWriter.TryComplete();
                }
            }, cancellationToken);
        }

        private int _frameCount = 0;
        Mat _image1 = new Mat();
        Mat _image2 = new Mat();


        private void StartCapture(TimeSpan publicationInterval, CancellationToken cancellationToken)
        {
            using var reader = this.InitCapture();
            var width = reader.FrameWidth;
            var height = reader.FrameHeight;
            int delayMs = (int)(500.0 / this.Fps);
            int errorCount = 0;

            while (!cancellationToken.IsCancellationRequested && !_stopping)
            {
                _frameCount++;

                Mat imageBuffer = (_frameCount % 2 == 0)? _image1: _image2;
                bool succesfullGrab;

                var startTime = DateTime.Now;
                // Grab single frame.
                var timestamp = DateTime.Now;

                bool success = reader.Read(imageBuffer);

                var endTime = DateTime.Now;
                LogTrace("Producer: frame-grab took {0} ms", (endTime - startTime).Milliseconds);

                succesfullGrab = success && !imageBuffer.Empty();

                if (succesfullGrab)
                {
                    var ctx = new VideoFrameContext(timestamp, _frameCount, this.Info);
                    var videoFrame = new VideoFrame(imageBuffer, ctx);
                    _frameBufferWriter.TryWrite(videoFrame);
                }
                else
                {
                    // If we've reached the end of the video, stop here.
                    if (IsContinuous)
                    {
                        errorCount++;
                        // If failed on live camera, try again.
                        _logger.LogWarning("Producer: null frame from live camera, continue! ({0} errors)", errorCount);

                        if (errorCount < 5)
                        {
                            _logger.LogWarning("Error in capture, retry");
                            continue;
                        }
                        else
                        {
                            _logger.LogWarning("Errorcount exceeded, restarting videocapture");
                            break;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Producer: null frame from video file, stop!");
                        _stopping = true;
                        // Break out of the loop to make sure we don't try grabbing more
                        // frames.
                        break;
                    }
                }
                Thread.Sleep(delayMs);
                //await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
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
            _stopping = true;
            if (_executionTask != null)
            {
                await _executionTask.ConfigureAwait(false);
                _executionTask = null;
            }
            _stopping = false;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _stopping = true;
                    StopProcessingAsync()?.Wait(2000);
                    _videoCapture?.Dispose();
                    _videoCapture = null;
                    _image1?.Dispose();
                    _image2?.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~VideoStream()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public void SetNextTrigger(DateTime timestamp, int triggerId)
        {
            if (_frameBufferReader.TryRead(out var frame))
            {
                Mat imageToPublish = PreprocessImage(frame.Image);

                // Package the image for submission.
                VideoFrame vframe = new VideoFrame(imageToPublish, frame.Metadata);
                vframe.TriggerId = triggerId;

                _logger.LogDebug($"Producer: do publishing of frame {frame.Metadata.Timestamp}; {this.Info.Id}; of trigger: {triggerId}");
                var writeResult = _outputWriter.TryWrite(vframe);
            }
            else
            {
                _logger.LogWarning("No frame available to publish");
            }
        }
    }
}
