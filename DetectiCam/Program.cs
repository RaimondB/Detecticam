#nullable enable

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DetectiCam.Core.Detection;
using DetectiCam.Core.VideoCapturing;
using DetectiCam.Core.ResultProcessor;

namespace CameraWatcher
{

    class Program
    {
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                ConfigureConfigDir(hostingContext, config);
                
                config.AddEnvironmentVariables(prefix: "CAMERAWATCH_");
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.AddOptions<MqttPublisherOptions>()
                    .Bind(hostContext.Configuration.GetSection(MqttPublisherOptions.MqttPublisher))
                    .ValidateDataAnnotations();

                var generateConfig = hostContext.Configuration.GetValue<bool>("gen-config");
                if (generateConfig)
                {
                    services.AddHostedService<WriteTemplateConfigService>();
                }
                else
                {
                    services.AddHttpClient();

                    services.AddHostedService<BatchedCameraWatcherService>();
                    services.AddSingleton<IBatchedDnnDetector, Yolo3BatchedDnnDetector>();
                    services.AddSingleton<IAsyncSingleResultProcessor, AnnotatedImagePublisher>();
                    services.AddSingleton<IAsyncSingleResultProcessor, WebhookPublisher>();
                    services.AddSingleton<IAsyncSingleResultProcessor, MqttPublisher>();

                    services.AddSingleton<MultiStreamBatchedProcessorPipeline,
                        MultiStreamBatchedProcessorPipeline>();
                }
            })
            .ConfigureLogging(logging =>
            {
                logging.AddConsole(c =>
                {
                    c.TimestampFormat = "[HH:mm:ss.fff] ";
                    c.IncludeScopes = false;
                });
            });

        private static void ConfigureConfigDir(HostBuilderContext hostingContext, IConfigurationBuilder config)
        {
            string? configPath = null;

            var basePathConfig = hostingContext.Configuration.GetValue<string>("configdir");
            if (!String.IsNullOrEmpty(basePathConfig))
            {
                configPath = Path.GetFullPath(basePathConfig);
                if (Directory.Exists(configPath))
                {
                    //Console.WriteLine($"ConfigDir for configuration:{configPath}");
                    config.AddJsonFile(Path.GetFullPath("appsettings.json", configPath));
                }
            }
            if (String.IsNullOrEmpty(configPath))
            {
                configPath = Directory.GetCurrentDirectory();
                //Console.WriteLine($"ConfigDir not specified, falling back to default location:{configPath}");
            }

            var Dict = new Dictionary<string, string>
                {
                    {"ConfigDir", configPath}
                };
            config.AddInMemoryCollection(Dict);
        }

        static async Task Main(string[] args)
        {
            await CreateHostBuilder(args).Build().RunAsync().ConfigureAwait(false);
        }
    }
}
