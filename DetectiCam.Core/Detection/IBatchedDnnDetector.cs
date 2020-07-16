using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DetectiCam.Core.Detection
{
    public interface IBatchedDnnDetector : IDisposable
    {
        public Task<DnnDetectedObject[][]> ClassifyObjects(IEnumerable<Mat> image, CancellationToken cancellationToken);
    }
}
