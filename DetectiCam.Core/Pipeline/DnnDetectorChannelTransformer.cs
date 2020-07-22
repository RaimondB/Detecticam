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
    public class DnnDetectorChannelTransformer : ChannelTransformer<IList<VideoFrame>, AnalysisResult>
    {
        private readonly IBatchedDnnDetector _detector;

        public DnnDetectorChannelTransformer(IBatchedDnnDetector detector,
            ChannelReader<IList<VideoFrame>> inputReader, ChannelWriter<AnalysisResult> outputWriter,
            ILogger logger) :
            base(inputReader, outputWriter, logger)
        {
            if (detector is null) throw new ArgumentNullException(nameof(detector));

            _detector = detector;
            _detector.Initialize();
            
            SetTransformer(this.DoAnalyzeFrames);
        }


        protected Task<AnalysisResult> DoAnalyzeFrames(IList<VideoFrame> frames, CancellationToken cancellationToken)
        {
            var output = new AnalysisResult(frames);

            try
            {
                Logger.LogDebug("DoAnalysis: started");

                var images = frames.Where(f => f.Image != null).Select(f => f.Image).ToList();

                DnnDetectedObject[][] result;

                if (images.Count > 0)
                {
                    var watch = new Stopwatch();
                    watch.Start();

                    result = _detector.ClassifyObjects(images);

                    watch.Stop();
                    Logger.LogInformation($"Classifiy-objects ms:{watch.ElapsedMilliseconds}");
                }
                else
                {
                    Logger.LogWarning("No images to run detector on");
                    result = Array.Empty<DnnDetectedObject[]>();
                }
                output.Analysis = result;

                Logger.LogDebug("DoAnalysis: done");
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ae)
#pragma warning restore CA1031 // Do not catch general exception types
            {
                output.Exception = ae;
                Logger.LogDebug("DoAnalysis: Exception from analysis task:{message}", ae.Message);
            }

            return Task.FromResult(output);
        }
    }
}
