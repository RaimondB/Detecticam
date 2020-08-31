using DetectiCam.Core.VideoCapturing;
using System.Threading;
using System.Threading.Tasks;

namespace DetectiCam.Core.ResultProcessor
{
    public interface IAsyncSingleResultProcessor
    {
        Task ProcessResultAsync(VideoFrame frame);
        Task StopProcessingAsync(CancellationToken cancellationToken);
    }
}
