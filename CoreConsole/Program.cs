#nullable enable

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VideoFrameAnalyzer;
using VideoFrameAnalyzeStd.Detection;

namespace CameraWatcher
{

    class Program
    {
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                var env = hostingContext.HostingEnvironment;
                string? configPath = null;

                var basePathConfig = hostingContext.Configuration.GetValue<string>("configdir");
                if (!String.IsNullOrEmpty(basePathConfig))
                {
                    configPath = Path.GetFullPath(basePathConfig);
                    if (Directory.Exists(configPath))
                    {
                        Console.WriteLine($"ConfigDir for additional configuration:{configPath}");
                        config.AddJsonFile(Path.GetFullPath("appsettings.json", configPath));
                    }
                }
                if(String.IsNullOrEmpty(configPath))
                {
                    configPath = Directory.GetCurrentDirectory();
                    Console.WriteLine($"ConfigDir not found, falling back to default location:{configPath}");
                }

                var Dict = new Dictionary<string, string>
                {
                    {"ConfigDir", configPath}
                };
                config.AddInMemoryCollection(Dict);

                config.AddEnvironmentVariables(prefix: "CAMERAWATCH_");
            })
            .ConfigureServices((hostContext, services) =>
            {
                var generateConfig = hostContext.Configuration.GetValue<bool>("gen-config");
                if (generateConfig)
                {
                    services.AddHostedService<WriteTemplateConfigService>();
                }
                else
                {
                    //services.AddHostedService<CameraWatcherService>();
                    //services.AddSingleton<IDnnDetector, Yolo3DnnDetector>();
                    //services.AddSingleton<MultiFrameGrabber<DnnDetectedObject[]>,
                    //    MultiFrameGrabber<DnnDetectedObject[]>>();
                    //services.AddSingleton<MultiFrameGrabber<DnnDetectedObject[]>,
                    //    MultiFrameGrabber<DnnDetectedObject[]>>();
                    services.AddHttpClient();
                    services.AddHostedService<BatchedCameraWatcherService>();
                    services.AddSingleton<IBatchedDnnDetector, Yolo3BatchedDnnDetector>();
                    services.AddSingleton<MultiStreamBatchedFrameGrabber<DnnDetectedObject[][]>,
                        MultiStreamBatchedFrameGrabber<DnnDetectedObject[][]>>();
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

        static async Task Main(string[] args)
        {
            await CreateHostBuilder(args).Build().RunAsync().ConfigureAwait(false);
        }
    }
}
