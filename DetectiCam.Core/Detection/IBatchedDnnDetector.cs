using OpenCvSharp;
using System;
using System.Collections.Generic;

namespace DetectiCam.Core.Detection
{
    public interface IBatchedDnnDetector : IDisposable
    {
        public IList<DnnDetectedObject[]> ClassifyObjects(IList<Mat> images, float detectionThreshold);
        public void Initialize();
    }
}
