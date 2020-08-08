using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DetectiCam.Core.Detection
{
    public interface IBatchedDnnDetector : IDisposable
    {
        public IList<DnnDetectedObject[]> ClassifyObjects(IEnumerable<Mat> image);
        public void Initialize();
    }
}
