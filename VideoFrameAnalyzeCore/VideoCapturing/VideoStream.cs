//#define TRACE_GRABBER

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

        private VideoCapture _videoCapture;
        public VideoCapture VideoCapture => _videoCapture;

        public string StreamName { get; }

        public bool IsContinuous { get; }

        public RotateFlags? RotateFlags { get; }

        private bool _stopping;
        private bool disposedValue;
        private Task _executionTask;
        private ILogger _logger;

        public VideoStream(ILogger logger, string streamName, string path, double fps = 0, bool isContinuous = true, RotateFlags? rotateFlags = null)
            : this(logger, new VideoStreamInfo() { Id = streamName, Path = path, Fps = fps, IsContinuous = isContinuous, RotateFlags = rotateFlags})
        {
        }

        public VideoStream(ILogger logger, VideoStreamInfo streamInfo)
        {
            Path = streamInfo.Path;
            _fps = streamInfo.Fps;
            StreamName = streamInfo.Id;
            IsContinuous = streamInfo.IsContinuous;
            RotateFlags = streamInfo.RotateFlags;
            _stopping = false;
            _logger = logger;
        }

        [Conditional("TRACE_GRABBER")]
        private void LogMessage(string format, params object[] args)
        {
            //ConcurrentLogger.WriteLine(String.Format(CultureInfo.InvariantCulture, format, args));
            _logger.LogInformation(String.Format(CultureInfo.InvariantCulture, format, args));
        }

        public VideoCapture InitCapture()
        {
            _videoCapture = new VideoCapture(Path);

            if (Fps == 0)
            {
                var rFpds = _videoCapture.Fps;

                _fps = (rFpds == 0) ? 30 : rFpds;
            }

            return _videoCapture;
        }

        public async Task StopProcessingAsync()
        {
            LogMessage("Producer: stopping, destroy reader and timer");
            _stopping = true;
            await _executionTask;
            _executionTask = null;
            _stopping = false;
        }

        public Task StartProcessingAsync(Channel<VideoFrame> outputChannel, TimeSpan publicationInterval)
        {
            _executionTask = Task.Run(async () =>
            {
                var writer = outputChannel.Writer;
                while (!_stopping)
                {
                    try
                    {
                        using (var reader = this.InitCapture())
                        {
                            var width = reader.FrameWidth;
                            var height = reader.FrameHeight;
                            int frameCount = 0;
                            int delayMs = (int)(500.0 / this.Fps);

                            Mat imageBuffer = new Mat();
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
                                //LogMessage("Producer: frame-grab took {0} ms", (endTime - startTime).Milliseconds);

                                if (!success)
                                {
                                    // If we've reached the end of the video, stop here.
                                    if (!IsContinuous)
                                    {
                                        LogMessage("Producer: null frame from video file, stop!");
                                        // This will call StopProcessing on a new thread.
                                        _stopping = true;
                                        // Break out of the loop to make sure we don't try grabbing more
                                        // frames.
                                        break;
                                    }
                                    else
                                    {
                                        // If failed on live camera, try again.
                                        LogMessage("Producer: null frame from live camera, continue!");
                                        continue;
                                    }
                                }

                                if (timestamp > nextpublicationTime)
                                {
                                    LogMessage("Producer: create frame to publish:");
                                    nextpublicationTime = timestamp + publicationInterval;
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

                                    // Package the image for submission.
                                    VideoFrameMetadata meta;
                                    meta.Index = frameCount;
                                    meta.Timestamp = timestamp;
                                    VideoFrame vframe = new VideoFrame(publishedImage, meta);

                                    LogMessage("Producer: do publishing");
                                    var writeResult = writer.TryWrite(vframe);
                                }
                                Thread.Sleep(delayMs);
                                //await Task.Delay(delayMs).ConfigureAwait(false);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception in processes videostream {name}, restarting", this.StreamName);
                    }
                }
                writer.Complete();
            });
            return _executionTask;
            // We reach this point by breaking out of the while loop. So we must be stopping.
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
