using OpenCvSharp;

namespace DetectiCam.Core.Detection
{
    public interface IDnnDetector
    {
        public DnnDetectedObject[] ClassifyObjects(Mat image, Rect boxToAnalyze);
    }
}
