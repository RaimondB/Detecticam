using System.ComponentModel.DataAnnotations;

namespace DetectiCam.Core.Detection
{
    public class CapturePublisherOptions
    {
        public const string CapturePublisher = "capture-publisher";

        [Required]
        public bool Enabled { get; set; } = true;
        [Required]
        public string CaptureRootDir { get; set; } = "/captures";
        [Required]
        public string CapturePattern { get; set; } = "{vsid}-{ts}.jpg";
    }
}
