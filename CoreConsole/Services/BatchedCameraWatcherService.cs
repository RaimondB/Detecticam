using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VideoFrameAnalyzer;
using VideoFrameAnalyzeStd.Detection;

namespace CameraWatcher
{
    public class BatchedCameraWatcherService : IHostedService
    {
        private IConfigurationRoot ConfigRoot;
        //private ReportConfig _config;
        private readonly ILogger _logger;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly IBatchedDnnDetector _detector;
        private string _captureOutputPath = null;

        private readonly MultiStreamBatchedFrameGrabber<DnnDetectedObject[][]> _grabber;

        public BatchedCameraWatcherService( ILogger<CameraWatcherService> logger,
                                        IBatchedDnnDetector detector,
                                        MultiStreamBatchedFrameGrabber<DnnDetectedObject[][]> grabber,
                                       IHostApplicationLifetime appLifetime,
                                       IConfiguration configRoot)
        {
            ConfigRoot = (IConfigurationRoot)configRoot;
            _logger = logger;
            _appLifetime = appLifetime;
            _detector = detector;
            _grabber = grabber;
            InitCaptureOutputPath(configRoot);
        }

        private void InitCaptureOutputPath(IConfiguration config)
        {
            var path = config.GetSection("capture-path").Get<string>();
            _captureOutputPath = Path.GetFullPath(path);
            if(!Directory.Exists(_captureOutputPath))
            {
                Directory.CreateDirectory(_captureOutputPath);
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _appLifetime.ApplicationStarted.Register(OnStarted, false);

            return Task.CompletedTask;
        }

        private async void OnStarted()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            _grabber.AnalysisFunction = OpenCVDNNYoloBatchDetect;

            _grabber.NewResultsAvailable += (s, e) =>
            {
                if (e.TimedOut)
                    _logger.LogWarning("Analysis function timed out.");
                else if (e.Exception != null)
                    _logger.LogError(e.Exception, "Analysis function threw an exception");
                else
                {
                    for(int index = 0; index < e.Frames.Count; index++)
                    {
                        var frame = e.Frames[index];
                        var analysis = e.Analysis[index];

                        using Mat inputImage = frame.Image;

                        _logger.LogInformation($"New result received for frame acquired at {frame.Metadata.Timestamp}. {analysis.Length} objects detected");
                        
                        foreach (var dObj in analysis)
                        {
                            _logger.LogInformation($"Detected: {dObj.Label} ; prob: {dObj.Probability}");
                        }

                        if (analysis.Length > 0 && analysis.Any(o => o.Label == "person"))
                        {
                            using (var result = Visualizer.AnnotateImage(frame.Image, analysis.ToArray()))
                            {
                                var filename = $"obj-{GetTimestampedSortable(frame.Metadata)}.jpg";
                                var filePath = Path.Combine(_captureOutputPath, filename);
                                Cv2.ImWrite(filePath, result);
                                _logger.LogInformation($"Interesting Detection Saved: {filename}");
                            }
                        } 
                    }
                }
            };

            // Tell grabber when to call API.
            // See also TriggerAnalysisOnPredicate
            _grabber.TriggerAnalysisOnInterval(TimeSpan.FromMilliseconds(3000));

            //_grabber.StartProcessingFileAsync(
            //    @"C:\Users\raimo\Downloads\Side Door - 20200518 - 164300_Trim.mp4",
            //    isContinuousStream: false, rotateFlags: RotateFlags.Rotate90Clockwise);

            //_grabber.StartProcessingFileAsync(
            //      @"C:\Users\raimo\Downloads\HIKVISION - DS-2CD2143G0-I - 20200518 - 194212-264.mp4",
            //      isContinuousStream: false);


            //_grabber.StartProcessingFileAsync(
            //    @"rtsp://cam-admin:M3s%21Ew9JEH%2A%23@foscam.home:88/videoSub",
            //    rotateFlags: RotateFlags.Rotate90Clockwise
            //    , overrideFPS: 15
            //);

            //_grabber.StartProcessingFileAsync(
            //    @"rtsp://admin:nCmDZx8U@192.168.2.125:554/Streaming/Channels/102",
            //    overrideFPS: 30);
            _grabber.StartProcessingAll();


        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Process received stop signal.");

            //await _grabber.StopProcessingAsync().ConfigureAwait(false);
            //_grabber.Dispose();
        }

        private static string GetTimestampedSortable(VideoFrameMetadata metaData)
        {
            return $"{metaData.Timestamp:yyyyMMddTHHmmss}-{metaData.Index:00000}";
        }

        private Task<DnnDetectedObject[][]> OpenCVDNNYoloBatchDetect(IList<VideoFrame> frames)
        {
            //if (image == null || image.Width <= 0 || image.Height <= 0)
            //{
            //    return Task.FromResult(Array.Empty<DnnDetectedObject>());
            //}

            var images = frames.Select(f => f.Image);


            Func<DnnDetectedObject[][]> detector = () =>
            {
                DnnDetectedObject[][] result;

                //try
                //{
                    var watch = new Stopwatch();
                    watch.Start();

                    result = _detector.ClassifyObjects(images);

                    watch.Stop();
                    _logger.LogInformation($"Classifiy-objects ms:{watch.ElapsedMilliseconds}");
                //}
                //catch (Exception ex)
                //{
                //    result = (DnnDetectedObject[][])new List<List<DnnDetectedObject>>(0);
                //    _logger.LogError(ex, $"Exception in analysis:{ex.Message}");
                //}

                return result;
            };

            //var result2 = detector();
            //return Task.FromResult(result2);

            return Task.Run(() => detector());
        }
    }
}
