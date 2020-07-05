//#define TRACE_GRABBER
#nullable enable

using Microsoft.Extensions.Logging;
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

    public class VideoStreamGrabber : IDisposable
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

        //public VideoStreamGrabber(ILogger logger, string streamName, string path, double fps = 0, bool isContinuous = true, RotateFlags? rotateFlags = null)
        //    : this(logger, new VideoStreamInfo() { Id = streamName, Path = path, Fps = fps, IsContinuous = isContinuous, RotateFlags = rotateFlags })
        //{
        //}

        public VideoStreamGrabber(ILogger logger, VideoStreamInfo streamInfo, Channel<VideoFrame> outputChannel)
        {
            if (logger is null) throw new ArgumentNullException(nameof(logger));
            if (streamInfo is null) throw new ArgumentNullException(nameof(streamInfo));

            Info = streamInfo;
            Path = streamInfo.Path;
            _fps = streamInfo.Fps;
            StreamName = streamInfo.Id;
            IsContinuous = streamInfo.IsContinuous;
            RotateFlags = streamInfo.RotateFlags;
            OutputChannel = outputChannel;

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
            _executionTask = Task.Run(async () =>
            {
                var writer = this.OutputChannel.Writer;
                while (!cancellationToken.IsCancellationRequested && !_stopping)
                {
                    try
                    {
                        await StartCaptureAsync(publicationInterval, writer, cancellationToken).ConfigureAwait(false);
                    }
#pragma warning disable CA1031 // Do not catch general exception types
                    catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
                    {
                        _logger.LogError(ex, "Exception in processes videostream {name}, restarting", this.StreamName);
                    }
                }
                writer.Complete();
            }, cancellationToken);
            // We reach this point by breaking out of the while loop. So we must be stopping.
        }

        private async Task StartCaptureAsync(TimeSpan publicationInterval, ChannelWriter<VideoFrame> writer, CancellationToken cancellationToken)
        {
            using var reader = this.InitCapture();
            var width = reader.FrameWidth;
            var height = reader.FrameHeight;
            int frameCount = 0;
            int delayMs = (int)(500.0 / this.Fps);
            int errorCount = 0;

            using Mat imageBuffer = new Mat();
            Mat publishedImage;

            var nextpublicationTime = DateTime.Now;
            while (!cancellationToken.IsCancellationRequested && !_stopping)
            {
                var startTime = DateTime.Now;
                // Grab single frame.
                var timestamp = DateTime.Now;

                bool success = reader.Read(imageBuffer);
                frameCount++;

                var endTime = DateTime.Now;
                LogTrace("Producer: frame-grab took {0} ms", (endTime - startTime).Milliseconds);

                if (!success)
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
                else if (timestamp > nextpublicationTime)
                {
                    LogTrace("Producer: create frame to publish:");
                    nextpublicationTime = timestamp + publicationInterval;

                    publishedImage = PreprocessImage(imageBuffer);

                    // Package the image for submission.
                    VideoFrameContext meta = new VideoFrameContext(timestamp, frameCount, this.Info);
                    VideoFrame vframe = new VideoFrame(publishedImage, meta);

                    LogTrace("Producer: do publishing");
                    var writeResult = writer.TryWrite(vframe);
                }
                //Thread.Sleep(delayMs);
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
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
    }
}
