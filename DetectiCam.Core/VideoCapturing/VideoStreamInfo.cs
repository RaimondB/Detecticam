using OpenCvSharp;
using System;
using DetectiCam.Core.Detection;
using DetectiCam.Core.ResultProcessor;

namespace DetectiCam.Core.VideoCapturing
{
    [Serializable]
    public class VideoStreamInfo
    {
        public string Id { get; set; } = default!;
        public string Path { get; set; } = default!;

        public double Fps { get; set; }
        public bool IsContinuous { get; set; } = true;

        public string? Rotate
        {
            get
            {
                return RotateFlags?.ToString();
            }

            set
            {
                if (Enum.TryParse<RotateFlags>(value, out var rotateFlags))
                {
                    RotateFlags = rotateFlags;
                }
                else
                {
                    RotateFlags = null;
                }
            }
        }

        public string? CallbackUrl { get; set; }

        public RotateFlags? RotateFlags { get; set; }

        /// <summary>
        /// The Region of Interest within which to report detections.
        /// </summary>
        public Region? ROI { get; set; }

        public ObjectWhiteList AdditionalObjectWhitelist { get; } = new ObjectWhiteList();
    }
}
