using DetectiCam.Core.Detection;
using DetectiCam.Core.VideoCapturing;
using DetectiCam.Core.Visualization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DetectiCam.Core.ResultProcessor
{
    public class AnnotatedImagePublisher : IAsyncSingleResultProcessor
    {
        private readonly string? _captureOutputPath;
        private readonly ILogger _logger;

        public AnnotatedImagePublisher(ILogger<AnnotatedImagePublisher> logger, IConfiguration config)
        {
            if (logger is null) throw new ArgumentNullException(nameof(logger));
            if (config is null) throw new ArgumentNullException(nameof(config));

            _logger = logger;

            var path = config.GetSection("capture-path").Get<string>();
            if (!String.IsNullOrEmpty(path))
            {
                _captureOutputPath = Path.GetFullPath(path);
                if (!Directory.Exists(_captureOutputPath))
                {
                    Directory.CreateDirectory(_captureOutputPath);
                }
            }
            else
            {
                _logger.LogWarning("capture-path is empty. No captures will be saved");
            }
        }

        private static string GetTimestampedSortable(VideoFrameContext metaData)
        {
            return $"{metaData.Timestamp:yyyyMMddTHHmmss}";
        }

        public Task ProcessResultAsync(VideoFrame frame, DnnDetectedObject[] results)
        {
            //If the output path is not set, skip this processor.
            if (String.IsNullOrEmpty(_captureOutputPath)) return Task.CompletedTask;

            if (frame is null) throw new ArgumentNullException(nameof(frame));
            if (results is null) throw new ArgumentNullException(nameof(results));

            _logger.LogInformation($"New result received for frame acquired at {frame.Metadata.Timestamp}. {results.Length} objects detected");

            foreach (var dObj in results)
            {
                _logger.LogInformation($"Detected: {dObj.Label} ; prob: {dObj.Probability}");
            }

            using var result = Visualizer.AnnotateImage(frame.Image, results.ToArray());
            var filename = $"obj-{GetTimestampedSortable(frame.Metadata)}.jpg";
            var filePath = Path.Combine(_captureOutputPath, filename);
            Cv2.ImWrite(filePath, result);
            _logger.LogInformation($"Interesting Detection Saved: {filename}");

            return Task.CompletedTask;
        }
    }
}
