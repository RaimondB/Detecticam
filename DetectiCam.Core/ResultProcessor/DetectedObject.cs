﻿using DetectiCam.Core.Detection;
using System;

namespace DetectiCam.Core.ResultProcessor
{
    public class DetectedObject
    {
        public int Index { get; set; }
        public string? Label { get; set; }
        public float Probability { get; set; }
        public BoundingBox? BoundingBox { get; set; }

        public static DetectedObject ConvertFrom(DnnDetectedObject dnnDetectedObject)
        {
            if (dnnDetectedObject is null) throw new ArgumentNullException(nameof(dnnDetectedObject));

            return new DetectedObject()
            {
                BoundingBox = BoundingBox.ConvertFrom(dnnDetectedObject.BoundingBox),
                Index = dnnDetectedObject.Index,
                Label = dnnDetectedObject.Label,
                Probability = dnnDetectedObject.Probability
            };
        }
    }
}
