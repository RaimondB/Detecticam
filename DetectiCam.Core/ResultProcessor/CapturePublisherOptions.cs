using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace DetectiCam.Core.Detection
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
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
