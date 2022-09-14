#nullable enable
using DetectiCam.Core.Detection;
using DetectiCam.Core.Pipeline;
using OpenCvSharp;
using System;
using System.Collections.Generic;

namespace DetectiCam.Core.VideoCapturing
{
    /// <summary> Metadata for a VideoFrame. </summary>
    public class VideoFrameContext
    {
        public DateTime Timestamp { get; }
        public int Index { get; }
        public VideoStreamInfo Info { get; }

        //For performance reasons it is allowed to direct attach the list.
        public IList<DnnDetectedObject>? AnalysisResult { get; set; }

        public VideoFrameContext(DateTime timestamp, int index, VideoStreamInfo info)
        {
            Timestamp = timestamp;
            Index = index;
            Info = info;
        }
    }

    /// <summary> A video frame produced by the Framegrabber.
    ///     This class encapsulates the image and metadata. </summary>
    public sealed class VideoFrame : IDisposable, ISyncTokenProvider
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

        public void Dispose()
        {
            Image.SafeDispose();
        }
    }
}
