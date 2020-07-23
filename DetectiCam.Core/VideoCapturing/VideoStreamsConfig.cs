using System.Collections.ObjectModel;

namespace DetectiCam.Core.VideoCapturing
{
    public class VideoStreamsConfigCollection : Collection<VideoStreamInfo>
    {
        public const string VideoStreamsConfigKey = "video-streams";
    }
}
