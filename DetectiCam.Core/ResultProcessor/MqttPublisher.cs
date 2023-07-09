using DetectiCam.Core.Common;
using DetectiCam.Core.Detection;
using DetectiCam.Core.VideoCapturing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;


namespace DetectiCam.Core.ResultProcessor
{
    public class MqttPublisher : ConfigurableService<MqttPublisher, MqttPublisherOptions>,
        IAsyncSingleResultProcessor
    {
        private readonly MqttClient? _client;
        private readonly bool _isEnabled;
        private readonly string? _topicPrefix;

        public MqttPublisher(ILogger<MqttPublisher> logger,
                             IOptions<MqttPublisherOptions> options) :
            base(logger, options)
        {
            _isEnabled = Options.Enabled;

            if (_isEnabled)
            {
                try
                {
                    if (!String.IsNullOrEmpty(Options.TopicPrefix))
                    {
                        _topicPrefix = Options.TopicPrefix;
                        if (!Options.TopicPrefix.EndsWith("/", StringComparison.OrdinalIgnoreCase))
                        {
                            _topicPrefix += "/";
                        }
                    }
                    else
                    {
                        _topicPrefix = "";
                    }

                    // create client instance 
                    _client = new MqttClient(Options.Server, Options.Port, false,
                        MqttSslProtocols.None,
                        null, null);

                    string clientId = String.IsNullOrEmpty(Options.ClientId) ? Guid.NewGuid().ToString() :
                        Options.ClientId;

                    _ = _client.Connect(clientId, Options.Username, Options.Password);

                    if (_client.IsConnected)
                    {
                        Logger.LogInformation("MQTT Client connected");
                    }
                }
                catch(Exception ex)
                {
                    Logger.LogError(ex, "Error in MQTTPUblisher");
                }
            }
        }

        public Task ProcessResultAsync(VideoFrame frame)
        {
            if (_isEnabled && _client != null && frame?.Metadata?.AnalysisResult is not null)
            {
                if (frame is null) throw new ArgumentNullException(nameof(frame));

                var output = new { detection = true,
                    detectedObjects = Options.IncludeDetectedObjects ? 
                        frame.Metadata.AnalysisResult
                            .OrderByDescending(r => r.Probability)
                            .Take(Options.TopDetectedObjectsLimit)
                            .Select((dob,i) => DetectedObject.ConvertFrom(dob))
                            .ToList()
                        : null 
                    };

                string strValue = JsonSerializer.Serialize(output,
                    new JsonSerializerOptions()
                    {
                        IgnoreNullValues = true
                    });

                string topic = $"{_topicPrefix}detect-i-cam/{frame.Metadata.Info.Id}/state";

                Logger.LogInformation("Published detection to:{0}", topic);
                // publish a message on "/home/temperature" topic with QoS 2 
                _client.Publish(topic,
                    Encoding.UTF8.GetBytes(strValue),
                    MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, false);
            }
            return Task.CompletedTask;
        }

        public Task StopProcessingAsync(CancellationToken cancellationToken)
        {
            _client?.Disconnect();
            return Task.CompletedTask;
        }
    }
}
