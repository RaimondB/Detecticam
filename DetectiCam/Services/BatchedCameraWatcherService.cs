using DetectiCam.Core.VideoCapturing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using static DetectiCam.Core.Common.ExceptionFilterUtility;

namespace DetectiCam
{
    public class BatchedCameraWatcherService : BackgroundService
    {
        private readonly ILogger _logger;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly MultiStreamBatchedProcessorPipeline _pipeline;

        public BatchedCameraWatcherService(ILogger<BatchedCameraWatcherService> logger,
                                        MultiStreamBatchedProcessorPipeline pipeline,
                                       IHostApplicationLifetime appLifetime)
        {
            if (logger is null) throw new ArgumentNullException(nameof(logger));
            if (pipeline is null) throw new ArgumentNullException(nameof(pipeline));
            if (appLifetime is null) throw new ArgumentNullException(nameof(appLifetime));

            _logger = logger;
            _appLifetime = appLifetime;
            _pipeline = pipeline;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(async () =>
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                // Tell grabber when to call API.
                // See also TriggerAnalysisOnPredicate
                _pipeline.TriggerAnalysisOnInterval(TimeSpan.FromMilliseconds(3000));

                var pipelineTask = _pipeline.StartProcessingAll(stoppingToken);
                await pipelineTask.ConfigureAwait(false);
            }
#pragma warning disable S2737 // "catch" clauses should do more than rethrow
            catch (Exception ex) when (False(() => _logger.LogCritical(ex, "Fatal error")))
            {
                throw;
            }
#pragma warning restore S2737 // "catch" clauses should do more than rethrow
            finally
            {
                // When the pipeline is done, we can exit the application
                _appLifetime.StopApplication();
            }
        }, stoppingToken);

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Process received stop signal.");

            await _pipeline.StopProcessingAsync().ConfigureAwait(false);

            _logger.LogInformation("Process stopped.");
        }

        public override void Dispose()
        {
            base.Dispose();

            _pipeline?.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
