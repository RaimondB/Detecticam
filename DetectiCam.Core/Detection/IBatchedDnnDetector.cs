using DetectiCam.Core.VideoCapturing;
using System;
using System.Collections.Generic;

namespace DetectiCam.Core.Detection
{
    public interface IBatchedDnnDetector : IDisposable
    {
        public IList<DnnDetectedObject[]> ClassifyObjects(IList<VideoFrame> frames, float detectionThreshold);
        public void Initialize();
    }
}
