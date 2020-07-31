namespace DetectiCam.Core.Detection
{
    public class CapturePublisherOptions
    {
        public const string CapturePublisher = "capture-publisher";

        public bool Enabled { get; set; } = true;
        public string CaptureRootDir { get; set; } = "/captures";
        public string CapturePattern { get; set; } = "{vsid}-{ts}.jpg";
    }
}
