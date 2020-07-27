namespace DetectiCam.Core.Detection
{
    public class CapturePublisherOptions
    {
        public const string CapturePublisher = "capture-publisher";

        public bool Enabled { get; set; } = true;
        public string CaptureRootDir { get; set; } = "/capture";
        public string CapturePattern { get; set; } = "obj-{ts}.jpg";
    }
}
