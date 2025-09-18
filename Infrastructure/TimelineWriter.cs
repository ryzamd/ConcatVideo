// Infrastructure/TimelineWriter.cs
using System.Text;
using System.Text.RegularExpressions;
using Domain.Entities;
using Domain.Interfaces;
using Models;
using Utilities;

namespace Infrastructure
{
    public sealed class TimelineWriter : ITimelineWriter
    {
        private readonly Config _config;
        public TimelineWriter(Config config) => _config = config;

        public void WriteTimeline(VideoPart part, string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);

            var baseNameNoExt = Path.GetFileNameWithoutExtension(part.OutputPath)!;
            var courseName = Regex.Replace(baseNameNoExt, @"\s*-\s*\[Part\s*\d+\]\s*$", "");
            var fileName = $"{courseName} - [Part {part.PartIndex:00}]-timeline.txt";
            var outPath = Path.Combine(outputDirectory, fileName);

            using var sw = new StreamWriter(outPath, false, new UTF8Encoding(false));

            var acc = TimeSpan.Zero;
            foreach (var clip in part.Clips)
            {
                var stamp = $"{(int)acc.TotalHours:00}:{acc.Minutes:00}:{acc.Seconds:00}";

                // ưu tiên tên gốc; fallback: tên đã rename
                var original = clip.OriginalName ?? Path.GetFileName(clip.RenamedPath);
                var title = MakeTitleFromOriginal(original);
                sw.WriteLine($"{stamp}: {title}");

                var durSec = Utils.GetVideoDurationSeconds(_config, clip.RenamedPath);
                acc = acc.Add(TimeSpan.FromSeconds(durSec));
            }
        }

        private static string MakeTitleFromOriginal(string? originalFileName)
        {
            if (string.IsNullOrWhiteSpace(originalFileName)) return string.Empty;

            var nameNoExt = Path.GetFileNameWithoutExtension(originalFileName);

            nameNoExt = Regex.Replace(nameNoExt, @"^\s*\d+\s*[\.\-_\)\]\:]*\s*", "");

            return nameNoExt.Trim();
        }
    }
}
