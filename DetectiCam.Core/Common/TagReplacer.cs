using DetectiCam.Core.VideoCapturing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DetectiCam.Core.Common
{
    public static class TagReplacer
    {
        private static readonly Regex _patternMatcher = new Regex(@"(\{.+\})", RegexOptions.Compiled);

        public static string ReplaceTags(string pattern, VideoFrame frame)
        {
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            if (frame is null) throw new ArgumentNullException(nameof(frame));

            var result = ReplaceTsToken(pattern, frame);
            result = ReplaceVsIdToken(result, frame);
            result = ReplaceDetectedObjectsToken(result, frame);
            result = ReplaceDateTimeTokens(result, frame);

            return result;
        }

        private static string GetTimestampedSortable(VideoFrameContext metaData)
        {
            return $"{metaData.Timestamp:yyyyMMddTHHmmss}";
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

        private static string ReplaceDetectedObjectsToken(string filePattern, VideoFrame frame)
        {
            if (filePattern.Contains("{dobj}", StringComparison.OrdinalIgnoreCase))
            {
                var objectList = String.Join(',', frame.Metadata.AnalysisResult.Select(d => d.Label).Distinct());

                return filePattern.Replace("{dobj}", objectList, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                return filePattern;
            }
        }

        private static string ReplaceDateTimeTokens(string filePattern, VideoFrame frame)
        {
            var ts = frame.Metadata.Timestamp;

            var result = _patternMatcher.Replace(filePattern, (m) => ts.ToString(m.Value.Trim('{', '}'), CultureInfo.CurrentCulture));

            return result;
        }

    }
}
