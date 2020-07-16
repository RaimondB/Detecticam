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
    public class BatchedCameraWatcherService : BackgroundService
    {
        private readonly ILogger _logger;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly IBatchedDnnDetector _detector;
        //private string? _captureOutputPath = null;
        private readonly IHttpClientFactory _clientFactory;

        private readonly MultiStreamBatchedPipeline _pipeline;
        //private Task? _resultWriterTask;

        public BatchedCameraWatcherService(ILogger<BatchedCameraWatcherService> logger,
                                        IBatchedDnnDetector detector,
                                        MultiStreamBatchedPipeline pipeline,
                                       IHostApplicationLifetime appLifetime,
                                       IConfiguration configRoot,
                                       IHttpClientFactory clientFactory)
        {
            if (logger is null) throw new ArgumentNullException(nameof(logger));
            if (detector is null) throw new ArgumentNullException(nameof(detector));
            if (pipeline is null) throw new ArgumentNullException(nameof(pipeline));
            if (appLifetime is null) throw new ArgumentNullException(nameof(appLifetime));
            if (configRoot is null) throw new ArgumentNullException(nameof(configRoot));
            if (clientFactory is null) throw new ArgumentNullException(nameof(clientFactory));

            _clientFactory = clientFactory;
            _logger = logger;
            _appLifetime = appLifetime;
            _detector = detector;
            _pipeline = pipeline;
            //InitCaptureOutputPath(configRoot);
        }

        //private void InitCaptureOutputPath(IConfiguration config)
        //{
        //    var path = config.GetSection("capture-path").Get<string>();
        //    if (!String.IsNullOrEmpty(path))
        //    {
        //        _captureOutputPath = Path.GetFullPath(path);
        //        if (!Directory.Exists(_captureOutputPath))
        //        {
        //            Directory.CreateDirectory(_captureOutputPath);
        //        }
        //    }
        //    else
        //    {
        //        _logger.LogWarning("capture-path is empty. No captures will be saved");
        //    }
        //}

        //public override Task StartAsync(CancellationToken cancellationToken)
        //{
        //    _appLifetime.ApplicationStarted.Register(OnStarted, false);

        //    return Task.CompletedTask;
        //}

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            _pipeline.AnalysisFunction = OpenCVDNNYoloBatchDetect;

            // Tell grabber when to call API.
            // See also TriggerAnalysisOnPredicate
            _pipeline.TriggerAnalysisOnInterval(TimeSpan.FromMilliseconds(3000));

            //_resultWriterTask = Task.Run(async () =>
            //{
            //    var resultReader = _pipeline.OutputChannel.Reader;

            //    await foreach(var result in resultReader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            //    {
            //        await ProcessAnalysisResult(result).ConfigureAwait(false);
            //    }
            //});

            return _pipeline.StartProcessingAll(stoppingToken);
        }

        //private async Task ProcessAnalysisResult(AnalysisResult<DnnDetectedObject[][]> e)
        //{
        //    if (e.TimedOut)
        //        _logger.LogWarning("Analysis function timed out.");
        //    else if (e.Exception != null)
        //        _logger.LogError(e.Exception, "Analysis function threw an exception");
        //    else
        //    {
        //        for (int index = 0; index < e.Frames.Count; index++)
        //        {
        //            var frame = e.Frames[index];
        //            var analysis = e.Analysis[index];

        //            using Mat inputImage = frame.Image;

        //            _logger.LogInformation($"New result received for frame acquired at {frame.Metadata.Timestamp}. {analysis.Length} objects detected");

        //            foreach (var dObj in analysis)
        //            {
        //                _logger.LogInformation($"Detected: {dObj.Label} ; prob: {dObj.Probability}");
        //            }

        //            if (!String.IsNullOrEmpty(_captureOutputPath))
        //            {
        //                if (analysis.Length > 0 && analysis.Any(o => o.Label == "person"))
        //                {
        //                    var info = frame.Metadata.Info;
        //                    _logger.LogInformation($"Interesting Detection For: {info.Id}");

        //                    using var result = Visualizer.AnnotateImage(frame.Image, analysis.ToArray());
        //                    var filename = $"obj-{GetTimestampedSortable(frame.Metadata)}.jpg";
        //                    var filePath = Path.Combine(_captureOutputPath, filename);
        //                    Cv2.ImWrite(filePath, result);
        //                    _logger.LogInformation($"Interesting Detection Saved: {filename}");

        //                    //Callback url configured, so execute it
        //                    var url = frame.Metadata.Info.CallbackUrl;
        //                    if (url != null)
        //                    {
        //                        _logger.LogInformation($"Trigger Callback Url Saved: {filename}");
        //                        using var client = _clientFactory.CreateClient();
        //                        await client.GetAsync(url).ConfigureAwait(false);
        //                    }
        //                }
        //            }
        //        }
        //    }
        //}

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Process received stop signal.");

            await _pipeline.StopProcessingAsync().ConfigureAwait(false);
        }

        private static string GetTimestampedSortable(VideoFrameContext metaData)
        {
            return $"{metaData.Timestamp:yyyyMMddTHHmmss}";
        }

        private Task<IList<DnnDetectedObject[]>> OpenCVDNNYoloBatchDetect(IList<VideoFrame> frames, CancellationToken cancellationToken)
        {
            async Task<IList<DnnDetectedObject[]>> detector ()
            {
                var images = frames.Where(f => f.Image != null).Select(f => f.Image).ToList();

                DnnDetectedObject[][] result;

                if (images.Count > 0)
                {
                    var watch = new Stopwatch();
                    watch.Start();

                    result = await _detector.ClassifyObjects(images, cancellationToken).ConfigureAwait(false);

                    watch.Stop();
                    _logger.LogInformation($"Classifiy-objects ms:{watch.ElapsedMilliseconds}");
                }
                else
                {
                    _logger.LogWarning("No images to run detector on");
                    result = Array.Empty<DnnDetectedObject[]>();
                }
                return result;
            };

            //var result2 = detector();
            //return Task.FromResult(result2);

            return Task.Run(detector, cancellationToken);
        }

        public override void Dispose()
        {
            base.Dispose();

            _pipeline?.Dispose();
            _detector?.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
