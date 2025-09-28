using Domain.Entities;
using Domain.Interfaces;
using Enums;
using Models;

namespace Infrastructure
{
    public class SubtitleMapper : ISubtitleMapper
    {
        private readonly Config _config;
        private readonly WorkDirs _work;
        private readonly RuntimeOptions _opts;
        private readonly IErrorLogger _logger;

        public SubtitleMapper(Config config, WorkDirs work, RuntimeOptions opts, IErrorLogger logger)
        {
            _config = config;
            _work = work;
            _opts = opts;
            _logger = logger;
        }

        public Dictionary<string, string> MapSubtitles(string subsDir, IEnumerable<VideoFile> videos)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var allSubs = Directory.Exists(subsDir)
                ? Directory.EnumerateFiles(subsDir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(p => p.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) ||
                                p.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : new List<string>();

            // group theo baseName
            var g = allSubs
                .GroupBy(p => Path.GetFileNameWithoutExtension(p)!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);

            var missing = new List<string>();
            var duplicate = new List<string>();

            foreach (var v in videos)
            {
                var baseName = Path.GetFileNameWithoutExtension(v.RenamedName)!;
                if (!g.TryGetValue(baseName, out var list) || list.Count == 0)
                {
                    missing.Add(baseName);
                    continue;
                }

                if (list.Count > 1)
                    duplicate.Add(baseName);

                // ưu tiên .srt
                var chosen = list.FirstOrDefault(x => x.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
                          ?? list.First(); // còn lại .vtt
                map[baseName] = chosen;
            }

            if (duplicate.Count > 0)
            {
                var msg = $"Found both .srt and .vtt for: {string.Join(", ", duplicate.Take(20))}";
                if (_opts.StrictMapping) { _logger.Error(msg); throw new Exception("Duplicate subtitle files."); }
                _logger.Warn(msg + " — prefer .srt.");
            }

            if (missing.Count > 0)
            {
                var msg = $"Missing subtitles for {missing.Count} file(s): {string.Join(", ", missing.Take(20))}";
                if (_opts.StrictMapping) { _logger.Error(msg); throw new Exception("Missing subtitles."); }

                if (_opts.OnMissingSubtitle == OnMissingSubtitleMode.CreateEmptyFile)
                {
                    foreach (var b in missing)
                    {
                        var empty = Path.Combine(subsDir, b + ".srt");
                        if (!File.Exists(empty))
                            File.WriteAllText(empty, string.Empty, new System.Text.UTF8Encoding(false));
                        map[b] = empty;
                    }
                    _logger.Warn(msg + " — created empty .srt.");
                }
                else
                {
                    _logger.Warn(msg);
                }
            }

            return map;
        }
    }
}
