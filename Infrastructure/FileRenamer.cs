// Infrastructure/FileRenamer.cs
using System.Text;
using System.Text.RegularExpressions;
using Domain.Entities;
using Domain.Interfaces;
using Enums;
using Models;
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
            // Đảm bảo đủ thư mục đích
            WorkDirs.EnsureDir(_work.Root);
            WorkDirs.EnsureDir(_work.VideosDir);
            WorkDirs.EnsureDir(_work.SubsDir);
            WorkDirs.EnsureDir(_work.LogsDir);
            WorkDirs.EnsureDir(_work.FilesDir);
            WorkDirs.EnsureDir(_work.ReportDir);

            // Nếu đã có bộ file rename sẵn trong work\videos\ thì trả về luôn (idempotent)
            bool existing = Directory.Exists(_work.VideosDir) &&
                            Directory.EnumerateFiles(_work.VideosDir)
                                .Any(f => Regex.IsMatch(Path.GetFileName(f)!, @"^\d+\..+"));
            if (existing)
            {
                return Directory.EnumerateFiles(_work.VideosDir)
                    .OrderBy(x => x, new NumericNameComparer())
                    .Select((p, i) => new VideoFile(i + 1, Path.GetFileName(p)!, Path.GetFileName(p)!, p))
                    .ToList();
            }

            int globalIndex = 0, subFolderKey = 0;
            var videoFiles = new List<VideoFile>();
            var missingRows = new List<string[]>();

            foreach (var sub in Utils.GetSubDirsSorted(parentFolder))
            {
                // ❗ Loại trừ hoàn toàn thư mục work\ và mọi thứ bên dưới
                if (IsUnderDirectory(sub, _work.Root) ||
                    string.Equals(Path.GetFileName(sub), "work", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                subFolderKey++;
                Console.WriteLine($"  - Processing sub-folder [{subFolderKey}] {Path.GetFileName(sub)}");

                var filesDest = Path.Combine(_work.FilesDir, Utils.SanitizeFileName(Path.GetFileName(sub)!));
                Utils.EnsureDir(filesDest);

                var files = Directory.EnumerateFiles(sub, "*", SearchOption.TopDirectoryOnly).ToList();

                // Video nguồn: KHÔNG lấy file kết quả dạng "xxx - [Part NN].mkv"
                var videos = files.Where(Utils.IsVideo)
                    .Where(p => !IsPartResultFile(p))
                    .OrderBy(p => NumericNameComparer.NumericPrefixOrDefault(Path.GetFileName(p)!))
                    .ThenBy(p => Path.GetFileName(p)!, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                // Subtitle nguồn (giữ nguyên logic map cũ)
                var subs = files.Where(Utils.IsSubtitle)
                    .OrderBy(p => NumericNameComparer.NumericPrefixOrDefault(Path.GetFileName(p)!))
                    .ThenBy(p => Path.GetFileName(p)!, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                var subMap = BuildSubtitleMap(videos, subs);

                foreach (var v in videos)
                {
                    globalIndex++;
                    var padWidth = globalIndex >= 1000 ? 4 : 3;
                    var newStem = Utils.ZeroPad(globalIndex, padWidth);

                    // copy/rename video
                    var vNewName = newStem + Path.GetExtension(v).ToLowerInvariant();
                    var vNewPath = Path.Combine(_work.VideosDir, vNewName);
                    File.Copy(v, vNewPath, true);
                    _excel.LogVideo(globalIndex, subFolderKey, Path.GetFileName(v)!, vNewName);
                    videoFiles.Add(new VideoFile(globalIndex, Path.GetFileName(v)!, vNewName, vNewPath));

                    // subtitle (giữ nguyên logic map & ghi)
                    if (subMap.TryGetValue(v, out var sPath) && !string.IsNullOrEmpty(sPath) && File.Exists(sPath))
                    {
                        var sNew = newStem + Path.GetExtension(sPath).ToLowerInvariant();
                        var sNewPath = Path.Combine(_work.SubsDir, sNew);

                        // If source is .vtt, convert to .srt
                        if (sPath.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase))
                        {
                            sNew = newStem + ".srt";
                            sNewPath = Path.Combine(_work.SubsDir, sNew);

                            // Convert VTT to SRT
                            if (ConvertVttToSrt(sPath, sNewPath))
                            {
                                _excel.LogSubtitle(globalIndex, subFolderKey, Path.GetFileName(sPath)!, sNew);
                            }
                            else
                            {
                                // If conversion failed, copy as-is
                                File.Copy(sPath, sNewPath, true);
                                _excel.LogSubtitle(globalIndex, subFolderKey, Path.GetFileName(sPath)!, sNew);
                            }
                        }
                        else
                        {
                            // Copy .srt directly
                            File.Copy(sPath, sNewPath, true);
                            _excel.LogSubtitle(globalIndex, subFolderKey, Path.GetFileName(sPath)!, sNew);
                        }
                    }
                    else
                    {
                        _excel.LogMissingSubtitle(globalIndex, subFolderKey, Path.GetFileName(v)!);
                    }
                }

                // copy extra files (giữ nguyên)
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

            // missing-subtitles.csv (giữ nguyên)
            if (missingRows.Count > 0)
            {
                var csv = Path.Combine(_work.ReportDir, "missing-subtitles.csv");
                var sb = new StringBuilder();
                sb.AppendLine("Video,Duration(s),Index,Note");
                foreach (var r in missingRows) sb.AppendLine(string.Join(",", r.Select(x => x.Replace(",", " "))));
                File.WriteAllText(csv, sb.ToString(), new UTF8Encoding(false));
            }

            return videoFiles;
        }

        // ===== Helpers giữ nguyên + bổ sung nhỏ =====

        private static bool IsUnderDirectory(string path, string root)
        {
            var full = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var baseDir = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return full.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || full.Equals(baseDir, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPartResultFile(string path)
        {
            if (!path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)) return false;
            var name = Path.GetFileNameWithoutExtension(path)!;
            return Regex.IsMatch(name, @"\s-\s\[Part\s*\d+\]\s*$", RegexOptions.IgnoreCase);
        }

        private Dictionary<string, string> BuildSubtitleMap(List<string> videos, List<string> subs)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var subsByNum = new Dictionary<int, List<string>>();
            foreach (var s in subs)
            {
                var num = NumericNameComparer.NumericPrefixOrDefault(Path.GetFileName(s)!);
                if (!subsByNum.TryGetValue(num, out var list)) { list = new List<string>(); subsByNum[num] = list; }
                list.Add(s);
            }
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in videos)
            {
                var vn = NumericNameComparer.NumericPrefixOrDefault(Path.GetFileName(v)!);
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

        private bool ConvertVttToSrt(string vttPath, string srtPath)
        {
            try
            {
                var lines = File.ReadAllLines(vttPath, Encoding.UTF8).ToList();

                // Remove WEBVTT header
                if (lines.Count > 0 && lines[0].Trim().Equals("WEBVTT", StringComparison.OrdinalIgnoreCase))
                {
                    lines.RemoveAt(0);
                    while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
                        lines.RemoveAt(0);
                }

                using var sw = new StreamWriter(srtPath, false, new UTF8Encoding(false));
                int globIndex = 0, i = 0;

                while (i < lines.Count)
                {
                    // Skip cue identifier if exists
                    if (i + 1 < lines.Count && HelperSRTVTT.IsVttTimelineLine(lines[i + 1]))
                        i++;

                    if (i >= lines.Count || !HelperSRTVTT.IsVttTimelineLine(lines[i]))
                    {
                        i++;
                        continue;
                    }

                    var timeLine = lines[i++];
                    if (!HelperSRTVTT.TryParseVttTimeline(timeLine, out var startMs, out var endMs))
                        continue;

                    var textLines = new List<string>();
                    while (i < lines.Count && !string.IsNullOrWhiteSpace(lines[i]))
                        textLines.Add(lines[i++]);

                    if (i < lines.Count && string.IsNullOrWhiteSpace(lines[i]))
                        i++;

                    globIndex++;
                    sw.WriteLine(globIndex);
                    sw.WriteLine($"{HelperSRTVTT.FormatSrtTimestamp(startMs)} --> {HelperSRTVTT.FormatSrtTimestamp(endMs)}");
                    foreach (var t in textLines)
                        sw.WriteLine(t);
                    sw.WriteLine();
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to convert VTT to SRT: {ex.Message}");
                return false;
            }
        }
    }
}
