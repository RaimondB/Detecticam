#nullable enable
using DetectiCam.Core.Pipeline;
using OpenCvSharp;
using System;

namespace DetectiCam.Core.VideoCapturing
{
    /// <summary> Metadata for a VideoFrame. </summary>
    public class VideoFrameContext
    {
        public DateTime Timestamp { get; }
        public int Index { get; }
        public VideoStreamInfo Info { get; }

        public VideoFrameContext(DateTime timestamp, int index, VideoStreamInfo info)
        {
            Timestamp = timestamp;
            Index = index;
            Info = info;
        }
    }

    /// <summary> A video frame produced by the Framegrabber.
    ///     This class encapsulates the image and metadata. </summary>
    public class VideoFrame : ISyncTokenProvider
    {
        /// <summary> Constructor. </summary>
        /// <param name="image">    The image captured by the camera. </param>
        /// <param name="metadata"> The metadata. </param>
        public VideoFrame(Mat image, VideoFrameContext metadata)
        {
            Image = image;
            Metadata = metadata;
        }

        /// <summary> Gets the image for the frame. </summary>
        /// <value> The image. </value>
        public Mat Image { get; }

        /// <summary> Gets the frame's metadata. </summary>
        /// <value> The metadata. </value>
        public VideoFrameContext Metadata { get; }

        public int? TriggerId { get; set; }

        public int? SyncToken => TriggerId;
    }
}
