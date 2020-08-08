using DetectiCam.Core.Detection;
using DetectiCam.Core.VideoCapturing;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DetectiCam.Core.Pipeline
{
    public class DnnDetectorChannelTransformer : ChannelTransformer<IList<VideoFrame>, IList<VideoFrame>>
    {
        private readonly IBatchedDnnDetector _detector;

        public DnnDetectorChannelTransformer(IBatchedDnnDetector detector,
            ChannelReader<IList<VideoFrame>> inputReader, ChannelWriter<IList<VideoFrame>> outputWriter,
            ILogger logger) :
            base(inputReader, outputWriter, logger)
        {
            if (detector is null) throw new ArgumentNullException(nameof(detector));

            _detector = detector;
            _detector.Initialize();
        }


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
                    var watch = new Stopwatch();
                    watch.Start();

                    result = _detector.ClassifyObjects(images);

                    watch.Stop();
                    Logger.LogInformation("Classifiy-objects ms:{classifyDuration}", watch.ElapsedMilliseconds);

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
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ae)
#pragma warning restore CA1031 // Do not catch general exception types
            {
                Logger.LogError("DoAnalysis: Exception from analysis task:{message}", ae.Message);
            }

            return new ValueTask<IList<VideoFrame>>(frames);
        }
    }
}
