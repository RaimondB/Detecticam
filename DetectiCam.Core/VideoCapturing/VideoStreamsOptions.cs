using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace DetectiCam.Core.VideoCapturing
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    [Serializable]
    public class VideoStreamsOptions : Collection<VideoStreamInfo>
    {
        public const string VideoStreams = "video-streams";
    }
}
