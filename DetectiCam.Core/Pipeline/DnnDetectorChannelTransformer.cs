using DetectiCam.Core.Common;
using DetectiCam.Core.Detection;
using DetectiCam.Core.VideoCapturing;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using static DetectiCam.Core.Common.ExceptionFilterUtility;

namespace DetectiCam.Core.Pipeline
{
    public class DnnDetectorChannelTransformer : ChannelTransformer<IList<VideoFrame>, IList<VideoFrame>>
    {
        private readonly IBatchedDnnDetector _detector;
        private readonly IHeartbeatReporter _heartbeatReporter;
        private readonly float _detectionThreshold;

        public DnnDetectorChannelTransformer(IBatchedDnnDetector detector,
            float detectionThreshold,
            ChannelReader<IList<VideoFrame>> inputReader,
            ChannelWriter<IList<VideoFrame>> outputWriter,
            IHeartbeatReporter heartbeatReporter,
            ILogger logger) :
            base(inputReader, outputWriter, logger)
        {
            if (detector is null) throw new ArgumentNullException(nameof(detector));
            if (heartbeatReporter is null) throw new ArgumentNullException(nameof(heartbeatReporter));

            _detectionThreshold = detectionThreshold;
            _heartbeatReporter = heartbeatReporter;
            _detector = detector;
            _detector.Initialize();
        }

        private readonly Stopwatch _stopwatch = new Stopwatch();

        protected override ValueTask<IList<VideoFrame>> ExecuteTransform(IList<VideoFrame> input, CancellationToken cancellationToken)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));

            try
            {
                Logger.LogDebug("DoAnalysis: started");

                IList<DnnDetectedObject[]> result;

                if (input.Count > 0)
                {
                    _stopwatch.Restart();

                    result = _detector.ClassifyObjects(input, _detectionThreshold);

                    _stopwatch.Stop();
                    Logger.LogInformation("Classifiy-objects ms:{classifyDuration} for {imageCount} images", _stopwatch.ElapsedMilliseconds, input.Count);

                    _heartbeatReporter.ReportHeartbeat();

                    for (int i = 0; i < result.Count; i++)
                    {
                        //Attachs results to the right videoframe
                        input[i].Metadata.AnalysisResult = result[i];
                    }
                }
                else
                {
                    Logger.LogWarning("No images to run detector on");
                }

                Logger.LogDebug("DoAnalysis: done");
            }
            catch (Exception ae) when (True(() =>
                    Logger.LogError("DoAnalysis: Exception from analysis task:{message}", ae.Message)))
#pragma warning disable S108 // Nested blocks of code should not be left empty
            {
            }
#pragma warning restore S108 // Nested blocks of code should not be left empty

            return new ValueTask<IList<VideoFrame>>(input);
        }
    }
}
