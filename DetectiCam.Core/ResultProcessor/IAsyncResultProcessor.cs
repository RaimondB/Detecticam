using DetectiCam.Core.Detection;
using DetectiCam.Core.VideoCapturing;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DetectiCam.Core.ResultProcessor
{
    public interface IAsyncSingleResultProcessor
    {
        Task ProcessResultAsync(VideoFrame frame, IList<DnnDetectedObject> results);
        Task StopProcessingAsync(CancellationToken cancellationToken);
    }
}
