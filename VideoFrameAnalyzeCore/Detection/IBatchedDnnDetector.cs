using OpenCvSharp;
using System.Collections.Generic;

namespace DetectiCam.Core.Detection
{
    public interface IBatchedDnnDetector
    {
        public DnnDetectedObject[][] ClassifyObjects(IEnumerable<Mat> image);
    }
}
