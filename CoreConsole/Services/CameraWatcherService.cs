using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VideoFrameAnalyzer;
using VideoFrameAnalyzeStd.Detection;

namespace CameraWatcher
{
    public class CameraWatcherService : IHostedService
    {
        private IConfigurationRoot ConfigRoot;
        //private ReportConfig _config;
        private readonly ILogger _logger;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly IDnnDetector _detector;
        private readonly IBatchedDnnDetector _bactchedDetector;

        private readonly MultiFrameGrabber<DnnDetectedObject[]> _grabber;

        public CameraWatcherService( ILogger<CameraWatcherService> logger,
                                     IDnnDetector detector,
                                     IBatchedDnnDetector batchedDetector,
                                     MultiFrameGrabber<DnnDetectedObject[]> grabber,
                                       IHostApplicationLifetime appLifetime,
                                       IConfiguration configRoot)
        {
            ConfigRoot = (IConfigurationRoot)configRoot;
            _logger = logger;
            _appLifetime = appLifetime;
            _detector = detector;
            _bactchedDetector = batchedDetector;
            _grabber = grabber;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            //_config = ConfigurationReader.ReadConfig(ConfigRoot);

            //var validationResult = _config.Validate();
            //if (validationResult.Count > 0)
            //{
            //    foreach (var result in validationResult)
            //    {
            //        _logger.LogError(result);
            //    }
            //}

            _appLifetime.ApplicationStarted.Register(OnStarted, false);

            return Task.CompletedTask;
        }

        private async void OnStarted()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            _grabber.AnalysisFunction = OpenCVDNNYoloPeopleDetect;

            _grabber.NewResultAvailable += (s, e) =>
            {
                if (e.TimedOut)
                    _logger.LogWarning("Analysis function timed out.");
                else if (e.Exception != null)
                    _logger.LogError(e.Exception, "Analysis function threw an exception");
                else
                {
                    using Mat inputImage = e.Frame.Image;

                    _logger.LogInformation($"New result received for frame acquired at {e.Frame.Metadata.Timestamp}. {e.Analysis.Length} objects detected");
                    foreach (var dObj in e.Analysis)
                    {
                        _logger.LogInformation($"Detected: {dObj.Label} ; prob: {dObj.Probability}");
                    }

                    if (e.Analysis.Length > 0 && e.Analysis.Any(o => o.Label == "person"))
                    {
                        using (var result = Visualizer.AnnotateImage(e.Frame.Image, e.Analysis))
                        {
                            var filename = $".\\captures\\obj-{GetTimestampedSortable(e.Frame.Metadata)}.jpg";
                            Cv2.ImWrite(filename, result);
                            _logger.LogInformation($"Interesting Detection Saved: {filename}");
                        }
                    }
                }
            };

            // Tell grabber when to call API.
            // See also TriggerAnalysisOnPredicate
            _grabber.TriggerAnalysisOnInterval(TimeSpan.FromMilliseconds(3000));

            //await grabber.StartProcessingFileAsync(
            //    @"C:\Users\raimo\Downloads\Side Door - 20200518 - 164300_Trim.mp4",
            //    isContinuousStream: false, rotateFlags: RotateFlags.Rotate90Clockwise);

            //await grabber.StartProcessingFileAsync(
            //      @"C:\Users\raimo\Downloads\HIKVISION - DS-2CD2143G0-I - 20200518 - 194212-264.mp4",
            //      isContinuousStream: false);


            _grabber.StartProcessingFileAsync(
                @"rtsp://cam-admin:M3s%21Ew9JEH%2A%23@foscam.home:88/videoSub",
                rotateFlags: RotateFlags.Rotate90Clockwise
                , overrideFPS: 15
            );

            _grabber.StartProcessingFileAsync(
                @"rtsp://admin:nCmDZx8U@192.168.2.125:554/Streaming/Channels/102",
                overrideFPS: 30);

            _grabber.StartProcessingAll();


        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Process received stop signal.");

            await _grabber.StopProcessingAsync().ConfigureAwait(false);
            _grabber.Dispose();
        }

        private static string GetTimestampedSortable(VideoFrameMetadata metaData)
        {
            return $"{metaData.Timestamp:yyyyMMddTHHmmss}-{metaData.Index:00000}";
        }

        private Task<DnnDetectedObject[]> OpenCVDNNYoloPeopleDetect(VideoFrame frame)
        {
            var image = frame.Image;
            if (image == null || image.Width <= 0 || image.Height <= 0)
            {
                return Task.FromResult(Array.Empty<DnnDetectedObject>());
            }

            Func<DnnDetectedObject[]> detector = () =>
            {
                DnnDetectedObject[] result;

                try
                {
                    var watch = new Stopwatch();
                    watch.Start();

                    result = _detector.ClassifyObjects(image, Rect.Empty);

                    watch.Stop();
                    _logger.LogInformation($"Classifiy-objects ms:{watch.ElapsedMilliseconds}");
                }
                catch (Exception ex)
                {
                    result = Array.Empty<DnnDetectedObject>();
                    _logger.LogError(ex, $"Exception in analysis:{ex.Message}");
                }

                return result;
            };

            //var result2 = detector();
            //return Task.FromResult(result2);

            return Task.Run(() => detector());
        }

    }
}
