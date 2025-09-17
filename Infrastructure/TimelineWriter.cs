// Infrastructure/TimelineWriter.cs
using Domain.Entities;
using Domain.Interfaces;
using Models;
using Utilities;

namespace Infrastructure
{
    public sealed class TimelineWriter : ITimelineWriter
    {
        private readonly Config _config;

        public TimelineWriter(Config config)
        {
            _config = config;
        }

        public void WriteTimeline(VideoPart part, string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);

            var baseNameNoExt = Path.GetFileNameWithoutExtension(part.OutputPath)!;
            var fileName = $"{baseNameNoExt}-timeline - [Part {part.PartIndex:00}].txt";
            var outPath = Path.Combine(outputDirectory, fileName);

            using var sw = new StreamWriter(outPath, false, new System.Text.UTF8Encoding(false));

            var acc = TimeSpan.Zero;
            foreach (var clip in part.Clips)
            {
                var stamp = $"{(int)acc.TotalHours:00}:{acc.Minutes:00}:{acc.Seconds:00}";
                sw.WriteLine($"{stamp}: {clip.OriginalName}");

                // tăng acc = duration clip (đo bằng ffprobe)
                var durSec = Utils.GetVideoDurationSeconds(_config, clip.RenamedPath);
                acc = acc.Add(TimeSpan.FromSeconds(durSec));
            }
        }
    }
}
