#nullable enable

using DetectiCam.Core.Detection;
using System;
using System.Collections.Generic;

namespace DetectiCam.Core.VideoCapturing
{
    public class AnalysisResult
    {
        public AnalysisResult(IList<VideoFrame> frames)
        {
            Frames = frames;
        }
        public IList<VideoFrame> Frames { get; }
        public IList<DnnDetectedObject[]> Analysis { get; set; } = default!;
        public bool TimedOut { get; set; } = false;
        public Exception? Exception { get; set; } = null;
    }
}
