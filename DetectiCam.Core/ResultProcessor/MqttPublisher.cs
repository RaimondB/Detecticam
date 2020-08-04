using DetectiCam.Core.Common;
using DetectiCam.Core.Detection;
using DetectiCam.Core.VideoCapturing;
using DetectiCam.Core.Visualization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
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
        private readonly bool _isEnabled = false;
        private readonly string? _topicPrefix;

        public MqttPublisher(ILogger<MqttPublisher> logger,
                             IOptions<MqttPublisherOptions> options) :
            base(logger, options)
        {
            _isEnabled = Options.Enabled;

            if (_isEnabled)
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

                _client.Connect(clientId, Options.Username, Options.Password);
            }
        }

        public Task ProcessResultAsync(VideoFrame frame, DnnDetectedObject[] results)
        {
            if (_isEnabled && _client != null)
            {
                if (frame is null) throw new ArgumentNullException(nameof(frame));
                if (results is null) throw new ArgumentNullException(nameof(results));

                string strValue = "{ \"detection\" : true }";
                string topic = $"{_topicPrefix}detect-i-cam/{frame.Metadata.Info.Id}/state";

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
