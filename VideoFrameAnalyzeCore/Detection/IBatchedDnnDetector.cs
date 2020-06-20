using OpenCvSharp;
using System.Collections.Generic;
using VideoFrameAnalyzer;

namespace VideoFrameAnalyzeStd.Detection
{
    public interface IBatchedDnnDetector
    {
        public DnnDetectedObject[][] ClassifyObjects(IEnumerable<Mat> image);
    }
}
