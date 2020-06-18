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
    public class WriteTemplateConfigService : IHostedService
    {
        private IConfigurationRoot ConfigRoot;
        //private ReportConfig _config;
        private readonly ILogger _logger;
        private readonly IHostApplicationLifetime _appLifetime;

        public WriteTemplateConfigService( ILogger<CameraWatcherService> logger,
                                       IHostApplicationLifetime appLifetime,
                                       IConfiguration configRoot)
        {
            ConfigRoot = (IConfigurationRoot)configRoot;
            _logger = logger;
            _appLifetime = appLifetime;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
//            _appLifetime.ApplicationStarted.Register(OnStarted, false);

            var outputDirectory = ((IConfiguration)ConfigRoot).GetValue<string>("ConfigDir");

            var targetFilePath = Path.GetFullPath("appsettings.json", outputDirectory);
            if (File.Exists(targetFilePath))
            {
                Console.WriteLine($"File {targetFilePath} already exists. Not creating template");
            }
            else
            {
                var sourceFilePath = Path.GetFullPath("appsettings.template.json", Directory.GetCurrentDirectory());
                File.Copy(sourceFilePath, targetFilePath);
            }

            _appLifetime.StopApplication();

            return Task.CompletedTask;
        }


        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Process exiting.");

            //await _grabber.StopProcessingAsync().ConfigureAwait(false);
            //_grabber.Dispose();
        }

    }
}
