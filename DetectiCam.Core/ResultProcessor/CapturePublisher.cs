using DetectiCam.Core.Detection;
using DetectiCam.Core.VideoCapturing;
using DetectiCam.Core.Visualization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DetectiCam.Core.ResultProcessor
{
    public class CapturePublisher : IAsyncSingleResultProcessor
    {
        private readonly string? _captureRootPath;
        private readonly ILogger _logger;
        private readonly CapturePublisherOptions _options;
        private readonly bool _isEnabled = true;

        public CapturePublisher(ILogger<CapturePublisher> logger, IConfiguration config,
            IOptions<CapturePublisherOptions> options)
        {
            if (logger is null) throw new ArgumentNullException(nameof(logger));
            if (config is null) throw new ArgumentNullException(nameof(config));
            if (options is null) throw new ArgumentNullException(nameof(options));

            _logger = logger;

            var path = config.GetSection("capture-path").Get<string>();
            if (!String.IsNullOrEmpty(path))
            {
                _logger.LogWarning("You are using a depreacted way to configure the Capture Publisher. Switch to \"capture-publisher\" section instead.");
                _captureRootPath = Path.GetFullPath(path);
                EnsureDirectoryPath(_captureRootPath);
            }

            try
            {
                _options = options.Value;
                _isEnabled = _options.Enabled;

                if (_isEnabled)
                {
                    if (!String.IsNullOrEmpty(_options.CaptureRootDir))
                    {
                        _captureRootPath = _options.CaptureRootDir;
                        EnsureDirectoryPath(_captureRootPath);
                    }
                }
            }
            catch (OptionsValidationException ex)
            {
                foreach (var failure in ex.Failures)
                {
                    _logger.LogError(failure);
                }
            }
        }

        private void EnsureDirectoryPath(String path)
        {
            try
            {
                String? folder = Path.GetDirectoryName(path);
                if (!String.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                {
                    // Try to create the directory.
                    DirectoryInfo di = Directory.CreateDirectory(folder);
                }
            }
            catch (IOException ioex)
            {
                _logger.LogError(ioex, $"Could not create directory path for filepath:{path}");
            }
        }

        private static string GetTimestampedSortable(VideoFrameContext metaData)
        {
            return $"{metaData.Timestamp:yyyyMMddTHHmmss}";
        }

        public Task ProcessResultAsync(VideoFrame frame, DnnDetectedObject[] results)
        {
            //If the output path is not set, skip this processor.
            if (String.IsNullOrEmpty(_captureRootPath)) return Task.CompletedTask;

            if (frame is null) throw new ArgumentNullException(nameof(frame));
            if (results is null) throw new ArgumentNullException(nameof(results));

            _logger.LogInformation($"New result received for frame acquired at {frame.Metadata.Timestamp}. {results.Length} objects detected");

            var labelStats = from r in results
                              group r by r.Label into g
                              select $"#{g.Key}:{g.Count()}" ;

            var stats = String.Join("; ", labelStats);
         
            _logger.LogInformation($"Detected: {stats}");


            var filename = _options.CapturePattern;
            filename = ReplaceTsToken(filename, frame);
            filename = ReplaceVsIdToken(filename, frame);
            filename = ReplaceDateTimeTokens(filename, frame);


            var filePath = Path.Combine(_captureRootPath, filename);
            EnsureDirectoryPath(filePath);

            using var result = Visualizer.AnnotateImage(frame.Image, results.ToArray());

            if (Cv2.ImWrite(filePath, result))
            {
                _logger.LogInformation("Interesting Detection Saved: {filename}", filename);
            }
            else
            {
                _logger.LogError("Error during write of file {filePath}", filePath);
            }
            return Task.CompletedTask;
        }

        private static string ReplaceTsToken(string filePattern, VideoFrame frame)
        {
            string ts = GetTimestampedSortable(frame.Metadata);

            return filePattern.Replace("{ts}", ts, StringComparison.OrdinalIgnoreCase);
        }

        private static string ReplaceVsIdToken(string filePattern, VideoFrame frame)
        {
            return filePattern.Replace("{vsid}", frame.Metadata.Info.Id, StringComparison.OrdinalIgnoreCase);
        }

        private static string ReplaceDateTimeTokens(string filePattern, VideoFrame frame)
        {
            var ts = frame.Metadata.Timestamp;

            Regex patternMatcher = new Regex(@"(\{.+\})");

            foreach(var m in patternMatcher.Matches(filePattern))
            {
                int a = 1;
            }

            var result = patternMatcher.Replace(filePattern, (m) => ts.ToString(m.Value.Trim('{','}'), CultureInfo.InvariantCulture));

            return result;
        }


        public Task StopProcessingAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
