using DetectiCam.Core.Detection;
using DetectiCam.Core.VideoCapturing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DetectiCam.Core.Common
{
    public abstract class ConfigurableService<TService, TOption> where TService : ConfigurableService<TService, TOption>
                                                       where TOption : class, new()
    {
        protected ILogger Logger { get; }
        protected TOption Options { get; }

        protected ConfigurableService(ILogger<TService> logger,
            IOptions<TOption> options)
        {
            if (logger is null) throw new ArgumentNullException(nameof(logger));
            if (options is null) throw new ArgumentNullException(nameof(options));

            Logger = logger;

            Options = GetValidatedOptions(options);
        }

        protected T GetValidatedOptions<T>(IOptions<T> options) where T: class,new()
        {
            if (options is null) throw new ArgumentNullException(nameof(options));

            try
            {
                var jsoptions = new JsonSerializerOptions();
                jsoptions.AddContext<JsonContext>();

                //using var memStream = new MemoryStream();
                //using var outputData = new StreamWriter(memStream, System.Text.Encoding.UTF8);
                Logger.LogDebug("Getting option value for type {type}:{value}", typeof(T).Name, System.Text.Json.JsonSerializer.Serialize<T>(options.Value, jsoptions));
                return options.Value;
                //return default(T);
            }
            catch (OptionsValidationException ex)
            {
                foreach (var failure in ex.Failures)
                {
                    Logger.LogError("Invalid configuration:{failure}",failure);
                }
                throw;
            }
        }
    }


    [JsonSerializable(typeof(CapturePublisherOptions))]
    [JsonSerializable(typeof(VideoStreamsOptions))]
    [JsonSerializable(typeof(Collection<VideoStreamInfo>))]
    [JsonSerializable(typeof(MqttPublisherOptions))]
    [JsonSerializable(typeof(DetectionOptions))]
    [JsonSerializable(typeof(Yolo3Options))]
    public partial class JsonContext : JsonSerializerContext
    { }

}
