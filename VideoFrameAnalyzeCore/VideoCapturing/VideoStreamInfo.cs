using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace VideoFrameAnalyzeStd.VideoCapturing
{
    public class VideoStreamInfo
    {
        public string Id { get; set; }
        public string Path { get; set; }

        public double Fps { get; set; }
        public bool IsContinuous { get; set; } = true;

        public string Rotate { 
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

        private RotateFlags? _rotateFlags = null;

        public RotateFlags? RotateFlags { 
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
