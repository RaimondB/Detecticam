#nullable enable

using OpenCvSharp;
using System;

namespace DetectiCam.Core.VideoCapturing
{
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
                return _rotateFlags?.ToString();
            }

            set
            {
                if (Enum.TryParse<RotateFlags>(value, out var rotateFlags))
                {
                    _rotateFlags = rotateFlags;
                }
                else
                {
                    _rotateFlags = null;
                }
            }
        }

        public Uri? CallbackUrl { get; set; }

        private RotateFlags? _rotateFlags = null;

        public RotateFlags? RotateFlags
        {
            get
            {
                return _rotateFlags;
            }
            set
            {
                _rotateFlags = value;
            }
        }
    }
}
