using System;
using System.Collections.Generic;
using System.Text;

namespace VideoFrameAnalyzeStd.Detection
{
    public class Yolo3Options
    {
        public const string Yolo3 = "yolo3";

        public string RootPath { get; set; }
        public string NamesFile { get; set; }
        public string ConfigFile { get; set; }
        public string WeightsFile { get; set; }
    }
}
