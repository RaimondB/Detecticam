#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using VideoFrameAnalyzer;

namespace VideoFrameAnalyzeCore.VideoCapturing
{
    /// <summary> Additional information for new frame events. </summary>
    /// <seealso cref="System.EventArgs"/>
    //public class NewFrameEventArgs : EventArgs
    //{
    //    public NewFrameEventArgs(VideoFrame frame)
    //    {
    //        Frame = frame;
    //    }
    //    public VideoFrame Frame { get; }
    //}

    ///// <summary> Additional information for new result events, which occur when an API call
    /////     returns. </summary>
    ///// <seealso cref="System.EventArgs"/>
    //public class NewResultEventArgs<TAnalysisResultType> : EventArgs
    //{
    //    public NewResultEventArgs(VideoFrame frame)
    //    {
    //        Frame = frame;
    //    }
    //    public VideoFrame Frame { get; }
    //    public TAnalysisResultType Analysis { get; set; } = default!;
    //    public bool TimedOut { get; set; } = false;
    //    public Exception? Exception { get; set; } = null;
    //}

    public class NewFramesEventArgs : EventArgs
    {
        public NewFramesEventArgs(IList<VideoFrame> frame)
        {
            Frame = frame;
        }
        public IList<VideoFrame> Frame { get; }
    }

    public class NewResultsEventArgs<TAnalysisResultType> : EventArgs
    {
        public NewResultsEventArgs(IList<VideoFrame> frames)
        {
            Frames = frames;
        }
        public IList<VideoFrame> Frames { get; }
        public TAnalysisResultType Analysis { get; set; } = default!;
        public bool TimedOut { get; set; } = false;
        public Exception? Exception { get; set; } = null;
    }
}
