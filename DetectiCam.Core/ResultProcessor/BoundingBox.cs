using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace DetectiCam.Core.ResultProcessor
{
    public class BoundingBox
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public static BoundingBox FromRect2D(Rect2d input)
        {
            return new BoundingBox()
            {
                X = (int)input.Left,
                Y = (int)input.Top,
                Width = (int)input.Width,
                Height = (int)input.Height
            };
        }
    }
}
