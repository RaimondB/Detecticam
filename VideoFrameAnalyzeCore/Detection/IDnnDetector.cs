using OpenCvSharp;
using VideoFrameAnalyzer;

namespace VideoFrameAnalyzeStd.Detection
{
    public interface IDnnDetector
    {
        public DnnDetectedObject[] ClassifyObjects(Mat image, Rect boxToAnalyze);
    }
}
