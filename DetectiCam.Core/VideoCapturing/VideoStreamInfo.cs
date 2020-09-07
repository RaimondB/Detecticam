﻿using OpenCvSharp;
using System;
using DetectiCam.Core.Detection;

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

        public ObjectWhiteList AdditionalObjectWhitelist { get; } = new ObjectWhiteList();
    }
}
