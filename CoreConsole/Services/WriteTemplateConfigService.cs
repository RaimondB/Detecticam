#nullable enable

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CameraWatcher
{
    public class WriteTemplateConfigService : IHostedService
    {
        private readonly IConfigurationRoot ConfigRoot;
        private readonly ILogger _logger;
        private readonly IHostApplicationLifetime _appLifetime;

        public WriteTemplateConfigService(ILogger<WriteTemplateConfigService> logger,
                                       IHostApplicationLifetime appLifetime,
                                       IConfiguration configRoot)
        {
            ConfigRoot = (IConfigurationRoot)configRoot;
            _logger = logger;
            _appLifetime = appLifetime;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
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

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Process exiting.");

            return Task.CompletedTask;
        }
    }
}
