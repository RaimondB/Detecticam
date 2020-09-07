using DetectiCam.Core.Detection;
using DetectiCam.Core.ResultProcessor;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using static DetectiCam.Core.Common.ExceptionFilterUtility;

namespace DetectiCam.Core.VideoCapturing
{
    public class AnalyzedVideoFrameChannelConsumer : ChannelConsumer<IList<VideoFrame>>
    {
        private readonly List<IAsyncSingleResultProcessor> _resultProcessors;
        private readonly ObjectWhiteList _objectWhitelist;
        private readonly Dictionary<string, HashSet<string>> _whitelistCache = new Dictionary<string, HashSet<string>>();

        public AnalyzedVideoFrameChannelConsumer(ChannelReader<IList<VideoFrame>> inputReader,
            IEnumerable<IAsyncSingleResultProcessor> resultProcessors,
            ObjectWhiteList objectWhitelist,
            ILogger logger)
            : base(inputReader, logger)
        {
            if (resultProcessors is null) throw new ArgumentNullException(nameof(resultProcessors));
            if (objectWhitelist is null) throw new ArgumentNullException(nameof(objectWhitelist));

            _resultProcessors = new List<IAsyncSingleResultProcessor>(resultProcessors);
            _objectWhitelist = objectWhitelist;
        }

        protected override Task ExecuteProcessorAsync([DisallowNull] IList<VideoFrame> input, CancellationToken cancellationToken)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));

            return ExecuteProcessorInternalAsync(input);
        }

        private async Task ExecuteProcessorInternalAsync(IList<VideoFrame> analyzedFrames)
        {
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

                        if (analysisResult.Count > 0 && HasDetectedWhitelistedObjects(frame))
                        {
                            Logger.LogInformation("Person detected for frame acquired at {timestamp}. Sending to result processors",
                                frame.Metadata.Timestamp);

                            foreach (var processor in _resultProcessors)
                            {
                                resultTasks.Add(processor.ProcessResultAsync(frame));
                            }
                        }
                    }
                }

                await Task.WhenAll(resultTasks).ConfigureAwait(false);
            }
            catch (Exception ex) when (True(() =>
                Logger.LogError(ex, "Exceptions during publication of detection results")))
#pragma warning disable S108 // Nested blocks of code should not be left empty
            { 
            }
#pragma warning restore S108 // Nested blocks of code should not be left empty
            finally
            {
                resultTasks.Clear();
                foreach (var frame in analyzedFrames)
                {
                    frame.Dispose();
                }
            }
        }

        private bool HasDetectedWhitelistedObjects(VideoFrame videoFrame)
        {
            var detections = videoFrame.Metadata.AnalysisResult.Select(d => d.Label).ToList();
            var whiteList = GetConsolidatedWhitelist(videoFrame);

            //Will return true if any of the whitelisted object was detected.
            return whiteList.Overlaps(detections);
        }

        private HashSet<string> GetConsolidatedWhitelist(VideoFrame frame)
        {
            var id = frame.Metadata.Info.Id;

            if (!_whitelistCache.TryGetValue(id, out HashSet<string>? whiteList))
            {
                whiteList = this._objectWhitelist.Concat(frame.Metadata.Info.AdditionalObjectWhitelist).Distinct().ToHashSet();

                if(whiteList.Count == 0)
                {
                    //If nothing is on the whitelist, we will default to detecting persons
                    whiteList.Add("person");
                }

                _whitelistCache.Add(id, whiteList);
            }
            return whiteList;
        }

        public override Task StopProcessingAsync(CancellationToken cancellationToken)
        {
            _resultProcessors.ForEach(async p => await p.StopProcessingAsync(cancellationToken).ConfigureAwait(false));
            return base.StopProcessingAsync(cancellationToken);
        }
    }
}
