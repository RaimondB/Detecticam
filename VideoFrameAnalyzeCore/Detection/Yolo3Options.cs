namespace DetectiCam.Core.Detection
{
    public class Yolo3Options
    {
        public const string Yolo3 = "yolo3";

        public string RootPath { get; set; } = default!;
        public string NamesFile { get; set; } = default!;
        public string ConfigFile { get; set; } = default!;
        public string WeightsFile { get; set; } = default!;
    }
}
