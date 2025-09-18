using Domain.Entities;
using Domain.Interfaces;
using Models;
using Utilities;

namespace Infrastructure
{
    public class SubtitleMerger : ISubtitleMerger
    {
        private readonly Config _config;
        private readonly IErrorLogger _logger;

        public SubtitleMerger(Config config, IErrorLogger logger)
        {
            _config = config;
            _logger = logger;
        }

        public void MergeSubtitles(VideoPart part, Dictionary<string, string> baseToSubtitlePath, string outputSrtPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputSrtPath)!);
            using var writer = new StreamWriter(outputSrtPath, false, new System.Text.UTF8Encoding(false));

            long cumulativeMs = 0;
            int globalIndex = 0;

            foreach (var clip in part.Clips)
            {
                var baseName = Path.GetFileNameWithoutExtension(clip.RenamedName);
                if (baseName.EndsWith(".norm", StringComparison.OrdinalIgnoreCase))
                    baseName = baseName[..^5];

                if (baseToSubtitlePath.TryGetValue(baseName, out var subPath))
                {
                    string srtPath = subPath.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)
                        ? subPath
                        : ConvertVttToSrt(subPath);

                    if (!string.IsNullOrEmpty(srtPath) && File.Exists(srtPath))
                        AppendSrtWithOffset(srtPath, cumulativeMs, writer, ref globalIndex);
                    else
                        _logger.Warn($"Subtitle '{Path.GetFileName(subPath)}' cannot be used; skipped.");
                }

                try
                {
                    var durSec = Utils.GetVideoDurationSeconds(_config, clip.RenamedPath);
                    cumulativeMs += (long)Math.Round(durSec * 1000.0);
                }
                catch (Exception ex)
                {
                    _logger.Warn($"ffprobe failed for '{clip.RenamedName}': {ex.Message}. Offset may be wrong.");
                }
            }
        }

        private string ConvertVttToSrt(string vttPath)
        {
            if (!vttPath.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase))
                return vttPath;

            var srtPath = Path.Combine(Path.GetDirectoryName(vttPath)!, Path.GetFileNameWithoutExtension(vttPath) + ".srt");
            try
            {
                var lines = File.ReadAllLines(vttPath, System.Text.Encoding.UTF8).ToList();
                if (lines.Count > 0 && lines[0].Trim().Equals("WEBVTT", StringComparison.OrdinalIgnoreCase))
                {
                    lines.RemoveAt(0);
                    while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0);
                }

                using var sw = new StreamWriter(srtPath, false, new System.Text.UTF8Encoding(false));
                int globIndex = 0, i = 0;
                while (i < lines.Count)
                {
                    if (i + 1 < lines.Count && HelperSRTVTT.IsVttTimelineLine(lines[i + 1])) i++;
                    if (i >= lines.Count || !HelperSRTVTT.IsVttTimelineLine(lines[i])) { i++; continue; }

                    var timeLine = lines[i++];
                    if (!HelperSRTVTT.TryParseVttTimeline(timeLine, out var startMs, out var endMs)) continue;

                    var textLines = new List<string>();
                    while (i < lines.Count && !string.IsNullOrWhiteSpace(lines[i])) textLines.Add(lines[i++]);
                    if (i < lines.Count && string.IsNullOrWhiteSpace(lines[i])) i++;

                    globIndex++;
                    sw.WriteLine(globIndex);
                    sw.WriteLine($"{HelperSRTVTT.FormatSrtTimestamp(startMs)} --> {HelperSRTVTT.FormatSrtTimestamp(endMs)}");
                    foreach (var t in textLines) sw.WriteLine(t);
                    sw.WriteLine();
                }
                return srtPath;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void AppendSrtWithOffset(string srtPath, long offsetMs, StreamWriter writer, ref int globalIndex)
        {
            var lines = File.ReadAllLines(srtPath, System.Text.Encoding.UTF8);
            int i = 0;
            while (i < lines.Length)
            {
                while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;
                if (i >= lines.Length) break;

                i++;

                if (i >= lines.Length) break;
                var timeline = lines[i++];
                if (!HelperSRTVTT.TryParseSrtTimeline(timeline, out var startMs, out var endMs))
                {
                    bool found = false;
                    while (i < lines.Length)
                    {
                        if (HelperSRTVTT.TryParseSrtTimeline(lines[i], out startMs, out endMs))
                        { i++; found = true; break; }
                        i++;
                    }
                    if (!found) continue;
                }

                var textLines = new List<string>();
                while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i])) textLines.Add(lines[i++]);
                if (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;

                globalIndex++;
                long ns = startMs + offsetMs, ne = endMs + offsetMs;
                writer.WriteLine(globalIndex);
                writer.WriteLine($"{HelperSRTVTT.FormatSrtTimestamp(ns)} --> {HelperSRTVTT.FormatSrtTimestamp(ne)}");
                foreach (var t in textLines) writer.WriteLine(t);
                writer.WriteLine();
            }
        }
    }
}
