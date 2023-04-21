using DetectiCam.Controllers;
using DetectiCam.Core.Common;
using DetectiCam.Core.Detection;
using DetectiCam.Core.ResultProcessor;
using DetectiCam.Core.VideoCapturing;
using Microsoft.AspNetCore.Builder;
using Microsoft.CodeAnalysis.Emit;
//using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;

namespace DetectiCam
{

    public static class Program
    {
        public static IHostBuilder ConfigureHostBuilder(IHostBuilder baseBuilder)
        {
            return baseBuilder
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                ConfigureConfigDir(hostingContext.Configuration, config);

                config.AddEnvironmentVariables(prefix: "CAMERAWATCH_");
            })
            //.ConfigureWebHostDefaults(webBuilder =>
            //{
            //    webBuilder.UseStartup<Startup>();
            //})
            .ConfigureServices((hostContext, services) =>
            {
                var generateConfig = hostContext.Configuration.GetValue<bool>("gen-config");
                if (generateConfig)
                {
                    services.AddHostedService<WriteTemplateConfigService>();
                }
                else
                {
                    ConfigureOptions(hostContext, services);

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

            [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2063", Justification = "Dynamicaccess is attributed")]
            static void ConfigureOptions(HostBuilderContext hostContext, IServiceCollection services)
            {
                // doing the alternative as a service https://learn.microsoft.com/en-us/dotnet/core/extensions/options
                //services.Configure<MqttPublisherOptions>(hostContext.Configuration.GetSection(MqttPublisherOptions.MqttPublisher));
                //applied DynamicallyAccessedMembers https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming
                _ = new MqttPublisherOptions();
                _ = new CapturePublisherOptions();
                _ = new VideoStreamsOptions();
                _ = new DetectionOptions();
                _ = new Yolo3Options();
                _ = new SnapshotOptions();

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
                services.AddOptions<SnapshotOptions>()
                    .Bind(hostContext.Configuration.GetSection(SnapshotOptions.Snapshot))
                    .ValidateDataAnnotations();
                services.AddOptions<MqttPublisherOptions>()
                    .Bind(hostContext.Configuration.GetSection(MqttPublisherOptions.MqttPublisher))
                    .ValidateDataAnnotations();

            }
        }

        private static void ConfigureConfigDir(IConfiguration hostingConfig, IConfigurationBuilder config)
        {
            string? configPath = null;

            var basePathConfig = hostingConfig.GetValue<string>("configdir");
            if (!String.IsNullOrEmpty(basePathConfig))
            {
                configPath = Path.GetFullPath(basePathConfig);
                if (Directory.Exists(configPath))
                {
                    //TODO: Find out how to get logger from static Host
                    Console.WriteLine($"ConfigDir for configuration:{configPath}");
                    config.AddJsonFile(Path.GetFullPath("appsettings.json", configPath));

                }
            }
            if (String.IsNullOrEmpty(configPath))
            {
                configPath = Directory.GetCurrentDirectory();
                Console.WriteLine($"ConfigDir not specified, falling back to default location:{configPath}");
            }

            var Dict = new Dictionary<string, string?>
                {
                    {"ConfigDir", configPath}
                };
            config.AddInMemoryCollection(Dict);
        }

        static void Main(string[] args)
        {
            var appBuilder = WebApplication.CreateBuilder(args);

            Program.ConfigureHostBuilder(appBuilder.Host);

            var app = appBuilder.Build();

            app.Urls.Add("http://localhost:8080");

            //var controller = new CameraController();

            app.MapGet("/", () => "Demo");

            //await CreateHostBuilder(args).Build().RunAsync().ConfigureAwait(false);
            app.Run();
        }
    }
}
