namespace DetectiCam.Core.Detection
{
    public class MqttPublisherOptions
    {
        public const string MqttPublisher = "mqtt-publisher";

        public bool Enabled { get; set; } 
        public string Server { get; set; } = default!;
        public int Port { get; set; } = 1883;
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? TopicPrefix { get; set; } 
        public string? ClientId { get; set; }
    }
}
