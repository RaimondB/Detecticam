using System.ComponentModel.DataAnnotations;

namespace DetectiCam.Core.Detection
{
    public class MqttPublisherOptions
    {
        public const string MqttPublisher = "mqtt-publisher";

        [Required]
        public bool Enabled { get; set; } = false;
        public string? Server { get; set; }

        public int Port { get; set; } = 1883;
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? TopicPrefix { get; set; }
        public string? ClientId { get; set; }
        public bool IncludeDetectedObjects { get; set; }
    }
}
