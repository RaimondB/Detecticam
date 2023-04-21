
using System.Diagnostics.CodeAnalysis;

namespace DetectiCam.Core.Detection
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public class DetectionOptions
    {
        public const string Detection = "detection";

        public float DetectionThreshold { get; set; } = 0.5f;

        public ObjectWhiteList ObjectWhiteList { get; } = new ObjectWhiteList();
    }
}
