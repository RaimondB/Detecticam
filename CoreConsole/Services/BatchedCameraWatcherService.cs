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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DetectiCam.Core.VideoCapturing;
using DetectiCam.Core.Visualization;
using DetectiCam.Core.Detection;

namespace CameraWatcher
{
    public class BatchedCameraWatcherService : IHostedService
    {
        private readonly ILogger _logger;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly IBatchedDnnDetector _detector;
        private string? _captureOutputPath = null;
        private IHttpClientFactory _clientFactory;

        private readonly MultiStreamBatchedPipeline<DnnDetectedObject[][]> _grabber;
        private Task? _resultWriterTask;

        public BatchedCameraWatcherService(ILogger<BatchedCameraWatcherService> logger,
                                        IBatchedDnnDetector detector,
                                        MultiStreamBatchedPipeline<DnnDetectedObject[][]> grabber,
                                       IHostApplicationLifetime appLifetime,
                                       IConfiguration configRoot,
                                       IHttpClientFactory clientFactory)
        {
            if (logger is null) throw new ArgumentNullException(nameof(logger));
            if (detector is null) throw new ArgumentNullException(nameof(detector));
            if (grabber is null) throw new ArgumentNullException(nameof(grabber));
            if (appLifetime is null) throw new ArgumentNullException(nameof(appLifetime));
            if (configRoot is null) throw new ArgumentNullException(nameof(configRoot));
            if (clientFactory is null) throw new ArgumentNullException(nameof(clientFactory));

            _clientFactory = clientFactory;
            _logger = logger;
            _appLifetime = appLifetime;
            _detector = detector;
            _grabber = grabber;
            InitCaptureOutputPath(configRoot);
        }

        private void InitCaptureOutputPath(IConfiguration config)
        {
            var path = config.GetSection("capture-path").Get<string>();
            if (!String.IsNullOrEmpty(path))
            {
                _captureOutputPath = Path.GetFullPath(path);
                if (!Directory.Exists(_captureOutputPath))
                {
                    Directory.CreateDirectory(_captureOutputPath);
                }
            }
            else
            {
                _logger.LogWarning("capture-path is empty. No captures will be saved");
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _appLifetime.ApplicationStarted.Register(OnStarted, false);

            return Task.CompletedTask;
        }

        private void OnStarted()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            _grabber.AnalysisFunction = OpenCVDNNYoloBatchDetect;


            // Tell grabber when to call API.
            // See also TriggerAnalysisOnPredicate
            _grabber.TriggerAnalysisOnInterval(TimeSpan.FromMilliseconds(3000));

            _resultWriterTask = Task.Run(async () =>
            {
                var resultReader = _grabber.OutputChannel.Reader;

                await foreach(var result in resultReader.ReadAllAsync(_appLifetime.ApplicationStopping).ConfigureAwait(false))
                {
                    await ProcessAnalysisResult(result).ConfigureAwait(false);
                }
            });

            _grabber.StartProcessingAll();
        }

        private async Task ProcessAnalysisResult(AnalysisResult<DnnDetectedObject[][]> e)
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

                    _logger.LogInformation($"New result received for frame acquired at {frame.Metadata.Timestamp}. {analysis.Length} objects detected");

                    foreach (var dObj in analysis)
                    {
                        _logger.LogInformation($"Detected: {dObj.Label} ; prob: {dObj.Probability}");
                    }

                    if (!String.IsNullOrEmpty(_captureOutputPath))
                    {
                        if (analysis.Length > 0 && analysis.Any(o => o.Label == "person"))
                        {
                            var info = frame.Metadata.Info;
                            _logger.LogInformation($"Interesting Detection For: {info.Id}");

                            using var result = Visualizer.AnnotateImage(frame.Image, analysis.ToArray());
                            var filename = $"obj-{GetTimestampedSortable(frame.Metadata)}.jpg";
                            var filePath = Path.Combine(_captureOutputPath, filename);
                            Cv2.ImWrite(filePath, result);
                            _logger.LogInformation($"Interesting Detection Saved: {filename}");

                            //Callback url configured, so execute it
                            var url = frame.Metadata.Info.CallbackUrl;
                            if (url != null)
                            {
                                _logger.LogInformation($"Trigger Callback Url Saved: {filename}");
                                using var client = _clientFactory.CreateClient();
                                await client.GetAsync(url).ConfigureAwait(false);
                            }
                        }
                    }
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Process received stop signal.");

            //await _grabber.StopProcessingAsync().ConfigureAwait(false);
            //_grabber.Dispose();
            return Task.CompletedTask;
        }

        private static string GetTimestampedSortable(VideoFrameContext metaData)
        {
            return $"{metaData.Timestamp:yyyyMMddTHHmmss}";
        }

        private Task<DnnDetectedObject[][]> OpenCVDNNYoloBatchDetect(IList<VideoFrame> frames)
        {
            DnnDetectedObject[][] detector()
            {
                var images = frames.Where(f => f.Image != null).Select(f => f.Image).ToList();

                DnnDetectedObject[][] result;

                var watch = new Stopwatch();
                watch.Start();

                result = _detector.ClassifyObjects(images);

                watch.Stop();
                _logger.LogInformation($"Classifiy-objects ms:{watch.ElapsedMilliseconds}");

                return result;
            }

            //var result2 = detector();
            //return Task.FromResult(result2);

            return Task.Run(() => detector());
        }
    }
}
