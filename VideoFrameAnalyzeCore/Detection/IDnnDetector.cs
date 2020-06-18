using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Text;
using VideoFrameAnalyzer;

namespace VideoFrameAnalyzeStd.Detection
{
    public interface IDnnDetector
    {
        public DnnDetectedObject[] ClassifyObjects(Mat image, Rect boxToAnalyze);
    }
}
