using Domain.Entities;
using Domain.Interfaces;
using Enums;
using Models;
using System.Text;
using System.Text.RegularExpressions;
using Utilities;

namespace Infrastructure
{
    public class FileRenamer : IFileRenamer
    {
        private readonly Config _config;
        private readonly WorkDirs _work;
        private readonly IErrorLogger _logger;
        private readonly IExcelLogger _excel;
        private readonly RuntimeOptions _opts;

        public FileRenamer(Config config, WorkDirs work, IErrorLogger logger, IExcelLogger excel, RuntimeOptions opts)
        {
            _config = config;
            _work = work;
            _logger = logger;
            _excel = excel;
            _opts = opts;
        }

        public IEnumerable<VideoFile> RenameAll(string parentFolder)
        {
            // Check for existing renamed
            bool existing = Directory.EnumerateFiles(_work.VideosDir)
                .Any(f => Regex.IsMatch(Path.GetFileName(f), @"^\d+\..+"));
            if (existing)
            {
                Console.WriteLine("  - Existing renamed videos detected, skipping rename stage.");
                return new List<VideoFile>();
            }

            // Count videos
            int totalVideos = 0;
            foreach (var sub in Utils.GetSubDirsSorted(parentFolder))
                foreach (var f in Directory.EnumerateFiles(sub, "*"))
                    if (Utils.IsVideo(f)) totalVideos++;
            int padWidth = totalVideos >= 1000 ? 4 : 3;
            using var bar = new ConsoleProgressBar("Rename");

            int processed = 0;
            int globalIndex = 0, subFolderKey = 0;
            var videoFiles = new List<VideoFile>();
            var missingRows = new List<string[]>();

            foreach (var sub in Utils.GetSubDirsSorted(parentFolder))
            {
                subFolderKey++;
                Console.WriteLine($"  - Processing sub-folder [{subFolderKey}] {Path.GetFileName(sub)}");
                var filesDest = Path.Combine(_work.FilesDir, Utils.SanitizeFileName(Path.GetFileName(sub)));
                Utils.EnsureDir(filesDest);

                var files = Directory.EnumerateFiles(sub, "*", SearchOption.TopDirectoryOnly).ToList();
                var videos = files.Where(Utils.IsVideo)
                    .OrderBy(p => NumericNameComparer.NumericPrefixOrDefault(Path.GetFileName(p)))
                    .ThenBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                var subs = files.Where(Utils.IsSubtitle)
                    .OrderBy(p => NumericNameComparer.NumericPrefixOrDefault(Path.GetFileName(p)))
                    .ThenBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                var subMap = BuildSubtitleMap(videos, subs);

                foreach (var v in videos)
                {
                    globalIndex++;
                    var newStem = Utils.ZeroPad(globalIndex, padWidth);

                    // Video
                    var vNewName = newStem + Path.GetExtension(v).ToLowerInvariant();
                    var vNewPath = Path.Combine(_work.VideosDir, vNewName);
                    File.Copy(v, vNewPath, true);
                    _excel.LogVideo(globalIndex, subFolderKey, Path.GetFileName(v), vNewName);
                    videoFiles.Add(new VideoFile(globalIndex, Path.GetFileName(v), vNewName, vNewPath));

                    // Subtitle
                    if (subMap.TryGetValue(v, out var sPath) && !string.IsNullOrEmpty(sPath) && File.Exists(sPath))
                    {
                        var sNew = newStem + Path.GetExtension(sPath).ToLowerInvariant();
                        var sNewPath = Path.Combine(_work.SubsDir, sNew);
                        File.Copy(sPath, sNewPath, true);
                        _excel.LogSubtitle(globalIndex, subFolderKey, Path.GetFileName(sPath), sNew);
                    }
                    else
                    {
                        // Missing subtitle
                        var sNewDefault = Path.Combine(_work.SubsDir, newStem + ".srt");
                        _excel.LogSubtitle(globalIndex, subFolderKey, "(missing)", Path.GetFileName(sNewDefault));
                        _logger.Warn($"Subtitle missing for video '{Path.GetFileName(v)}' -> expected index {newStem}");
                        double dur = 0;
                        try { dur = Utils.GetVideoDurationSeconds(_config, v); } catch { }
                        missingRows.Add(new[] {
                            Path.GetFileName(v),
                            dur.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                            newStem, "missing" });

                        if (_opts.StrictMapping)
                            throw new Exception($"StrictMapping enabled and subtitle missing for {Path.GetFileName(v)}");

                        if (_opts.OnMissingSubtitle == OnMissingSubtitleMode.CreateEmptyFile)
                        {
                            try { File.WriteAllText(sNewDefault, string.Empty, new UTF8Encoding(false)); }
                            catch (Exception ex) { _logger.Warn($"Failed to create empty subtitle '{sNewDefault}': {ex.Message}"); }
                        }
                    }

                    processed++;
                    if (totalVideos > 0)
                        bar.Report((double)processed / totalVideos);
                }

                // Copy other files
                foreach (var other in files.Where(f => !Utils.IsVideo(f) && !Utils.IsSubtitle(f)))
                {
                    try
                    {
                        var dest = Path.Combine(filesDest, Path.GetFileName(other)!);
                        File.Copy(other, dest, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"Failed to copy extra file '{other}': {ex.Message}");
                    }
                }
            }

            // Write missing-subtitles.csv if any
            if (missingRows.Count > 0)
            {
                var csv = Path.Combine(_work.ReportDir, "missing-subtitles.csv");
                var sb = new StringBuilder();
                sb.AppendLine("Video,Duration(s),Index,Note");
                foreach (var r in missingRows) sb.AppendLine(string.Join(",", r.Select(x => x.Replace(",", " "))));
                File.WriteAllText(csv, sb.ToString(), new UTF8Encoding(false));
            }

            bar.Done();
            return videoFiles;
        }

        // Builds map from video file to its subtitle (prefers .srt):contentReference[oaicite:14]{index=14}.
        private Dictionary<string, string> BuildSubtitleMap(List<string> videos, List<string> subs)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var subsByNum = new Dictionary<int, List<string>>();
            foreach (var s in subs)
            {
                var num = NumericNameComparer.NumericPrefixOrDefault(Path.GetFileName(s));
                if (!subsByNum.TryGetValue(num, out var list)) { list = new List<string>(); subsByNum[num] = list; }
                list.Add(s);
            }
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in videos)
            {
                var vn = NumericNameComparer.NumericPrefixOrDefault(Path.GetFileName(v));
                if (subsByNum.TryGetValue(vn, out var cand))
                {
                    var s = cand.FirstOrDefault(x => !used.Contains(x));
                    if (s != null) { map[v] = s; used.Add(s); }
                }
            }
            for (int i = 0; i < videos.Count; i++)
            {
                if (map.ContainsKey(videos[i])) continue;
                if (i < subs.Count)
                {
                    var s = subs[i];
                    if (!used.Contains(s)) { map[videos[i]] = s; used.Add(s); }
                }
            }
            return map;
        }

        IEnumerable<VideoFile> IFileRenamer.RenameAll(string parentFolder)
        {
            throw new NotImplementedException();
        }
    }
}
