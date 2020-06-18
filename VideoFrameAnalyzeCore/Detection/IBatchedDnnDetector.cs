using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Text;
using VideoFrameAnalyzer;

namespace VideoFrameAnalyzeStd.Detection
{
    public interface IBatchedDnnDetector
    {
        public DnnDetectedObject[][] ClassifyObjects(IEnumerable<Mat> image);
    }
}
