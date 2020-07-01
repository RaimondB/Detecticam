#nullable enable
using OpenCvSharp;

namespace VideoFrameAnalyzer
{
    public class DnnDetectedObject
    {
        public int Index { get; set; }
        public string Label { get; set; } = default!;
        public float Probability { get; set; }
        public Rect2d BoundingBox { get; set; }
        public Scalar Color { get; set; }
    }
}
