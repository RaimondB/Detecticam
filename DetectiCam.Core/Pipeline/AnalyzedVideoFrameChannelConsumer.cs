using DetectiCam.Core.ResultProcessor;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DetectiCam.Core.VideoCapturing
{
    public class AnalyzedVideoFrameChannelConsumer : ChannelConsumer<IList<VideoFrame>>
    {
        private readonly List<IAsyncSingleResultProcessor> _resultProcessors;


        public AnalyzedVideoFrameChannelConsumer(ChannelReader<IList<VideoFrame>> inputReader,
            IEnumerable<IAsyncSingleResultProcessor> resultProcessors,
            ILogger logger)
            : base(inputReader, logger)
        {
            if (resultProcessors is null) throw new ArgumentNullException(nameof(resultProcessors));

            _resultProcessors = new List<IAsyncSingleResultProcessor>(resultProcessors);
        }

        protected override async Task ExecuteProcessorAsync([DisallowNull] IList<VideoFrame> analyzedFrames, CancellationToken cancellationToken)
        {
            if (analyzedFrames is null) throw new ArgumentNullException(nameof(analyzedFrames));

            var resultTasks = new List<Task>();
            try
            {

                for (int index = 0; index < analyzedFrames.Count; index++)
                {
                    var frame = analyzedFrames[index];
                    var analysisResult = frame.Metadata.AnalysisResult;
                    if (analysisResult != null)
                    {
                        Logger.LogInformation("New result received for {streamId} frame acquired at {timestamp}.",
                            frame.Metadata.Info.Id, frame.Metadata.Timestamp);

                        if (analysisResult.Count > 0 && analysisResult.Any(o => o.Label == "person"))
                        {
                            Logger.LogInformation("Person detected for frame acquired at {timestamp}. Sending to result processors",
                                frame.Metadata.Timestamp);

                            foreach (var processor in _resultProcessors)
                            {
                                resultTasks.Add(processor.ProcessResultAsync(frame, analysisResult));
                            }
                        }
                    }
                }

                await Task.WhenAll(resultTasks).ConfigureAwait(false);
                resultTasks.Clear();
                foreach(var frame in analyzedFrames)
                {
                    frame.Dispose();
                }
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
            {
                Logger.LogError(ex, "Exceptions during publication of detection results");
            }
        }

        public override Task StopProcessingAsync(CancellationToken cancellationToken)
        {
            _resultProcessors.ForEach(async p => await p.StopProcessingAsync(cancellationToken).ConfigureAwait(false));
            return base.StopProcessingAsync(cancellationToken);
        }
    }
}
