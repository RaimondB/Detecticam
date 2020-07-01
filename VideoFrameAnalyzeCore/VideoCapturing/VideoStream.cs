#define TRACE_GRABBER

using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using VideoFrameAnalyzer;

namespace VideoFrameAnalyzeStd.VideoCapturing
{

    public class VideoStream : IDisposable
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

        public VideoStream(ILogger logger, string streamName, string path, double fps = 0, bool isContinuous = true, RotateFlags? rotateFlags = null)
            : this(logger, new VideoStreamInfo() { Id = streamName, Path = path, Fps = fps, IsContinuous = isContinuous, RotateFlags = rotateFlags })
        {
        }

        public VideoStream(ILogger logger, VideoStreamInfo streamInfo)
        {
            Info = streamInfo;

#pragma warning disable CA1062 // Validate arguments of public methods
            Path = streamInfo.Path;
#pragma warning restore CA1062 // Validate arguments of public methods
            _fps = streamInfo.Fps;
            StreamName = streamInfo.Id;
            IsContinuous = streamInfo.IsContinuous;
            RotateFlags = streamInfo.RotateFlags;
            _stopping = false;
            _logger = logger;
        }

        private void LogInformation(string format, params object[] args)
        {
            _logger.LogInformation(String.Format(CultureInfo.InvariantCulture, format, args));
        }

        private void LogWarning(string format, params object[] args)
        {
            _logger.LogWarning(String.Format(CultureInfo.InvariantCulture, format, args));
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
                    LogInformation($"Init Fps from stream:{this.Info.Id} at {rFpds}");
                    _fps = rFpds;

                }
                else
                {
                    LogInformation($"Fps {rFpds} invalid Init Fps from stream:{this.Info.Id}. Fallback to 30 fps");
                    _fps = 30;
                }
            }
            else
            {
                LogInformation($"Init Forced Fps from stream:{this.Info.Id} at {Fps}");
            }

            return _videoCapture;
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

        public void StartProcessingAsync(Channel<VideoFrame> outputChannel, TimeSpan publicationInterval)
        {
            _executionTask = Task.Run(async () =>
            {
                var writer = outputChannel.Writer;
                while (!_stopping)
                {
                    await RobustCapture(publicationInterval, writer).ConfigureAwait(false);
                }
                writer.Complete();
            });
            // We reach this point by breaking out of the while loop. So we must be stopping.
        }

        private async Task RobustCapture(TimeSpan publicationInterval, ChannelWriter<VideoFrame> writer)
        {
            try
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
                while (!_stopping)
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
                            LogWarning("Producer: null frame from live camera, continue! ({0} errors)", errorCount);

                            if (errorCount < 5)
                            {
                                LogWarning("Error in capture, retry");
                                continue;
                            }
                            else
                            {
                                LogWarning("Errorcount exceeded, restarting videocapture");
                                break;
                            }
                        }
                        else
                        {
                            LogWarning("Producer: null frame from video file, stop!");
                            // This will call StopProcessing on a new thread.
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
                        VideoFrameMetadata meta = new VideoFrameMetadata(timestamp, frameCount, this.Info);
                        VideoFrame vframe = new VideoFrame(publishedImage, meta);

                        LogTrace("Producer: do publishing");
                        var writeResult = writer.TryWrite(vframe);
                    }
                    //Thread.Sleep(delayMs);
                    await Task.Delay(delayMs).ConfigureAwait(false);
                }
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
            {
                _logger.LogError(ex, "Exception in processes videostream {name}, restarting", this.StreamName);
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

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _videoCapture?.Dispose();
                    _videoCapture = null;
                    _executionTask?.Wait();
                    _executionTask = null;
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
