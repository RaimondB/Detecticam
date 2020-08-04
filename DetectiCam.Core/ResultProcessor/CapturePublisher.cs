using DetectiCam.Core.Common;
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
    public class CapturePublisher : ConfigurableService<CapturePublisher, CapturePublisherOptions>, IAsyncSingleResultProcessor
    {
        private readonly string? _captureRootPath;
        private readonly bool _isEnabled = true;

        public CapturePublisher(ILogger<CapturePublisher> logger, IConfiguration config,
            IOptions<CapturePublisherOptions> options) :
            base(logger, options)
        {
            if (config is null) throw new ArgumentNullException(nameof(config));

            var path = config.GetSection("capture-path").Get<string>();
            if (!String.IsNullOrEmpty(path))
            {
                Logger.LogWarning("You are using a depreacted way to configure the Capture Publisher. Switch to \"capture-publisher\" section instead.");
                _captureRootPath = Path.GetFullPath(path);
                EnsureDirectoryPath(_captureRootPath);
            }

            _isEnabled = Options.Enabled;

            if (_isEnabled)
            {
                if (!String.IsNullOrEmpty(Options.CaptureRootDir))
                {
                    _captureRootPath = Options.CaptureRootDir;
                    EnsureDirectoryPath(_captureRootPath);
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
                Logger.LogError(ioex, $"Could not create directory path for filepath:{path}");
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

            Logger.LogInformation($"New result received for frame acquired at {frame.Metadata.Timestamp}. {results.Length} objects detected");

            var labelStats = from r in results
                              group r by r.Label into g
                              select $"#{g.Key}:{g.Count()}" ;

            var stats = String.Join("; ", labelStats);
         
            Logger.LogInformation($"Detected: {stats}");


            var filename = Options.CapturePattern;
            filename = ReplaceTsToken(filename, frame);
            filename = ReplaceVsIdToken(filename, frame);
            filename = ReplaceDateTimeTokens(filename, frame);


            var filePath = Path.Combine(_captureRootPath, filename);
            EnsureDirectoryPath(filePath);

            using var result = Visualizer.AnnotateImage(frame.Image, results.ToArray());

            if (Cv2.ImWrite(filePath, result))
            {
                Logger.LogInformation("Interesting Detection Saved: {filename}", filename);
            }
            else
            {
                Logger.LogError("Error during write of file {filePath}", filePath);
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

            var result = patternMatcher.Replace(filePattern, (m) => ts.ToString(m.Value.Trim('{','}'), CultureInfo.InvariantCulture));

            return result;
        }


        public Task StopProcessingAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
