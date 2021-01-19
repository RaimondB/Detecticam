using DetectiCam.Core.Common;
using DetectiCam.Core.Detection;
using DetectiCam.Core.ResultProcessor;
using DetectiCam.Core.VideoCapturing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace DetectiCam
{

    public static class Program
    {
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                ConfigureConfigDir(hostingContext, config);

                config.AddEnvironmentVariables(prefix: "CAMERAWATCH_");
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.AddOptions<MqttPublisherOptions>()
                    .Bind(hostContext.Configuration.GetSection(MqttPublisherOptions.MqttPublisher))
                    .ValidateDataAnnotations();
                services.AddOptions<CapturePublisherOptions>()
                    .Bind(hostContext.Configuration.GetSection(CapturePublisherOptions.CapturePublisher))
                    .ValidateDataAnnotations();
                services.AddOptions<VideoStreamsOptions>()
                    .Bind(hostContext.Configuration.GetSection(VideoStreamsOptions.VideoStreams))
                    .ValidateDataAnnotations();
                services.AddOptions<DetectionOptions>()
                    .Bind(hostContext.Configuration.GetSection(DetectionOptions.Detection))
                    .ValidateDataAnnotations();
                services.AddOptions<Yolo3Options>()
                    .Bind(hostContext.Configuration.GetSection(Yolo3Options.Yolo3))
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
                    services.AddSingleton<IBatchedDnnDetector, YoloBatchedDnnDetector>();
                    services.AddSingleton<IAsyncSingleResultProcessor, CapturePublisher>();
                    services.AddSingleton<IAsyncSingleResultProcessor, WebhookPublisher>();
                    services.AddSingleton<IAsyncSingleResultProcessor, MqttPublisher>();
                    services.AddSingleton<HeartbeatHealthCheck<MultiStreamBatchedProcessorPipeline>>();

                    services.AddSingleton<MultiStreamBatchedProcessorPipeline,
                        MultiStreamBatchedProcessorPipeline>();
                }
            })
            .ConfigureLogging(logging =>
            {
                _ = logging.AddSimpleConsole(c =>
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
                    //TODO: Find out how to get logger from static Host
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
