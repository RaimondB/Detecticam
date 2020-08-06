using DetectiCam.Core.ResultProcessor;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DetectiCam.Core.VideoCapturing
{
    public class AnalysisResultsChannelConsumer: ChannelConsumer<AnalysisResult>
    {
        private readonly List<IAsyncSingleResultProcessor> _resultProcessors;


        public AnalysisResultsChannelConsumer(ChannelReader<AnalysisResult> inputReader,
            IEnumerable<IAsyncSingleResultProcessor> resultProcessors,
            ILogger logger) 
            : base (inputReader, logger)
        {
            if (resultProcessors is null) throw new ArgumentNullException(nameof(resultProcessors));

            _resultProcessors = new List<IAsyncSingleResultProcessor>(resultProcessors);
        }

        protected override async Task ExecuteProcessorAsync(AnalysisResult e, CancellationToken cancellationToken)
        {
            if (e.TimedOut)
                Logger.LogWarning("Analysis function timed out.");
            else if (e.Exception != null)
                Logger.LogError(e.Exception, "Analysis function threw an exception");
            else
            {
                var resultTasks = new List<Task>();

                for (int index = 0; index < e.Frames.Count; index++)
                {
                    var frame = e.Frames[index];
                    if (e.Analysis != null)
                    {
                        var analysis = e.Analysis[index];

                        using Mat inputImage = frame.Image;

                        Logger.LogInformation("New result received for {vsid} frame acquired at {timestamp}.",
                            frame.Metadata.Info.Id, frame.Metadata.Timestamp);

                        if (analysis.Length > 0 && analysis.Any(o => o.Label == "person"))
                        {
                            Logger.LogInformation("Person detected for frame acquired at {timestamp}. Sending to result processors",
                                frame.Metadata.Timestamp);

                            foreach (var processor in _resultProcessors)
                            {
                                resultTasks.Add(processor.ProcessResultAsync(frame, analysis));
                            }
                        }
                    }
                }

                try
                {
                    await Task.WhenAll(resultTasks).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
                {
                    Logger.LogError(ex, "Exceptions during publication of detection results");
                }
            }
        }

        public override Task StopProcessingAsync(CancellationToken cancellationToken)
        {
            _resultProcessors.ForEach(async p => await p.StopProcessingAsync(cancellationToken).ConfigureAwait(false));
            return base.StopProcessingAsync(cancellationToken);
        }
    }
}
