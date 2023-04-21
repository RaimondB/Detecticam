using System.Diagnostics.CodeAnalysis;

namespace DetectiCam.Core.Detection
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public class Yolo3Options
    {
        public const string Yolo3 = "yolo3";

        public string RootPath { get; set; } = "/abaddefaultyolo-data";
        public string NamesFile { get; set; } = "coco.names";
        public string ConfigFile { get; set; } = "yolov3.cfg";
        public string WeightsFile { get; set; } = "yolov3.weights";
    }
}