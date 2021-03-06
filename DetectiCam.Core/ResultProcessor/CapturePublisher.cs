﻿using DetectiCam.Core.Common;
using DetectiCam.Core.Detection;
using DetectiCam.Core.VideoCapturing;
using DetectiCam.Core.Visualization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DetectiCam.Core.ResultProcessor
{
    public class CapturePublisher : ConfigurableService<CapturePublisher, CapturePublisherOptions>, IAsyncSingleResultProcessor
    {
        private readonly string _captureRootPath;
        private readonly bool _isEnabled = true;

        public CapturePublisher(ILogger<CapturePublisher> logger, IConfiguration config,
            IOptions<CapturePublisherOptions> options) :
            base(logger, options)
        {
            if (config is null) throw new ArgumentNullException(nameof(config));

            _isEnabled = Options.Enabled;
            _captureRootPath = Options.CaptureRootDir;

            var path = config.GetSection("capture-path").Get<string>();
            if (!String.IsNullOrEmpty(path))
            {
                Logger.LogWarning("You are using a depreacted way to configure the Capture Publisher. Switch to \"capture-publisher\" section instead.");
                _captureRootPath = Path.GetFullPath(path);
                EnsureDirectoryPath(_captureRootPath);
            }
            else
            {
                if (_isEnabled && !String.IsNullOrEmpty(Options.CaptureRootDir))
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
                    Directory.CreateDirectory(folder);
                }
            }
            catch (IOException ioex)
            {
                Logger.LogError(ioex, "Could not create directory path for filepath:{capturepath}", path);
            }
        }

        public Task ProcessResultAsync(VideoFrame frame)
        {
            //If not enabled, skip this processor.
            if (!_isEnabled) return Task.CompletedTask;
            if (frame is null) throw new ArgumentNullException(nameof(frame));
            var results = frame.Metadata.AnalysisResult;
            if (results is null) throw new InvalidOperationException("An analysis result is expected");

            Logger.LogInformation("New result received for frame acquired at {timestamp}. {detectionCount} objects detected",
                frame.Metadata.Timestamp, results.Count);

            var labelStats = from r in results
                             group r by r.Label into g
                             select $"#{g.Key}:{g.Count()}";

            var stats = String.Join("; ", labelStats);

            Logger.LogInformation("Detected: {detectionstats}", stats);


            var filename = TokenReplacer.ReplaceTokens(Options.CapturePattern, frame);

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

        public Task StopProcessingAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
