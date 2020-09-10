
namespace DetectiCam.Core.Detection
{
    public class DetectionOptions
    {
        public const string Detection = "detection";

        public float DetectionThreshold { get; set; } = 0.5f;

        public ObjectWhiteList ObjectWhiteList { get; } = new ObjectWhiteList();
    }
}
