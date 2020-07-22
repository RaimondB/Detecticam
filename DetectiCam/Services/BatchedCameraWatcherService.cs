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

        private readonly MultiStreamBatchedProcessorPipeline _pipeline;

        public BatchedCameraWatcherService(ILogger<BatchedCameraWatcherService> logger,
                                        IBatchedDnnDetector detector,
                                        MultiStreamBatchedProcessorPipeline pipeline,
                                       IHostApplicationLifetime appLifetime,
                                       IConfiguration configRoot)
        {
            if (logger is null) throw new ArgumentNullException(nameof(logger));
            if (detector is null) throw new ArgumentNullException(nameof(detector));
            if (pipeline is null) throw new ArgumentNullException(nameof(pipeline));
            if (appLifetime is null) throw new ArgumentNullException(nameof(appLifetime));
            if (configRoot is null) throw new ArgumentNullException(nameof(configRoot));

            _logger = logger;
            _appLifetime = appLifetime;
            _detector = detector;
            _pipeline = pipeline;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            // Tell grabber when to call API.
            // See also TriggerAnalysisOnPredicate
            _pipeline.TriggerAnalysisOnInterval(TimeSpan.FromMilliseconds(3000));

            var pipelineTask = _pipeline.StartProcessingAll(stoppingToken);
            await pipelineTask.ConfigureAwait(false);

            // When the pipeline is done, we can exit the application
            _appLifetime.StopApplication();
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Process received stop signal.");

            await _pipeline.StopProcessingAsync().ConfigureAwait(false);
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
