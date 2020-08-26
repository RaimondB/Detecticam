﻿using DetectiCam.Core.Common;
using DetectiCam.Core.Detection;
using DetectiCam.Core.VideoCapturing;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

        public DnnDetectorChannelTransformer(IBatchedDnnDetector detector,
            ChannelReader<IList<VideoFrame>> inputReader,
            ChannelWriter<IList<VideoFrame>> outputWriter,
            IHeartbeatReporter heartbeatReporter,
            ILogger logger) :
            base(inputReader, outputWriter, logger)
        {
            if (detector is null) throw new ArgumentNullException(nameof(detector));
            if (heartbeatReporter is null) throw new ArgumentNullException(nameof(heartbeatReporter));

            _heartbeatReporter = heartbeatReporter;

            _detector = detector;
            _detector.Initialize();
        }

        private readonly Stopwatch _stopwatch = new Stopwatch();

        protected override ValueTask<IList<VideoFrame>> ExecuteTransform(IList<VideoFrame> frames, CancellationToken cancellationToken)
        {
            if (frames is null) throw new ArgumentNullException(nameof(frames));

            try
            {
                Logger.LogDebug("DoAnalysis: started");

                var images = frames.Where(f => f.Image != null).Select(f => f.Image).ToList();

                IList<DnnDetectedObject[]> result;

                if (images.Count > 0)
                {
                    _stopwatch.Restart();

                    result = _detector.ClassifyObjects(images);

                    _stopwatch.Stop();
                    Logger.LogInformation("Classifiy-objects ms:{classifyDuration} for {imageCount} images", _stopwatch.ElapsedMilliseconds, images.Count);

                    _heartbeatReporter.ReportHeartbeat();

                    for (int i = 0; i < result.Count; i++)
                    {
                        //Attachs results to the right videoframe
                        frames[i].Metadata.AnalysisResult = result[i];
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
            {
            }

            return new ValueTask<IList<VideoFrame>>(frames);
        }
    }
}
