using System.Text;
using System.Text.Json;
using Domain.Interfaces;
using Domain.Models;
using Models;

namespace Infrastructure
{
    public sealed class ProcessingLogService : IProcessingLogService
    {
        private readonly WorkDirs _work;
        private readonly IErrorLogger _logger;

        public ProcessingLogService(WorkDirs work, IErrorLogger logger)
        {
            _work = work;
            _logger = logger;
        }

        private string JsonPath => Path.Combine(_work.LogsDir, "processing.json");

        public bool Exists() => File.Exists(JsonPath);

        public ProcessingLog Load()
        {
            var json = File.ReadAllText(JsonPath, new UTF8Encoding(false));
            return JsonSerializer.Deserialize<ProcessingLog>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            ) ?? new ProcessingLog();
        }

        public void Save(ProcessingLog log)
        {
            Directory.CreateDirectory(_work.LogsDir);
            var json = JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(JsonPath, json, new UTF8Encoding(false));
        }

        public ValidationResult Validate(ProcessingLog log)
        {
            var r = new ValidationResult
            {
                WorkFolderExists = Directory.Exists(_work.Root),
                HasProcessingLog = Exists()
            };

            // --- Renamed VIDEOS (ephemeral): chỉ kiểm tra khi thư mục còn tồn tại
            if (Directory.Exists(_work.VideosDir))
            {
                var currentRenamed = Directory.EnumerateFiles(_work.VideosDir, "*.mp4")
                    .Select(Path.GetFileName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // phát hiện file mới (không có trong log)
                foreach (var cur in currentRenamed)
                    if (!log.Rename.Videos.Any(x => x.Renamed.Equals(cur, StringComparison.OrdinalIgnoreCase)))
                        r.Added.RenamedVideos.Add(cur!);
            }

            // --- Renamed SUBTITLES
            if (Directory.Exists(_work.SubsDir))
            {
                foreach (var s in log.Rename.Subtitles.Select(x => x.Renamed))
                    if (!File.Exists(Path.Combine(_work.SubsDir, s))) r.Missing.RenamedSubtitles.Add(s);
            }
            else
            {
                // Mất cả thư mục -> coi như thiếu toàn bộ để còn heal
                foreach (var s in log.Rename.Subtitles.Select(x => x.Renamed))
                    r.Missing.RenamedSubtitles.Add(s);
            }

            // --- Detect ADDED (file mới xuất hiện so với log)
            var curVideos = Directory.Exists(_work.VideosDir)
                ? Directory.EnumerateFiles(_work.VideosDir, "*.mp4").Select(Path.GetFileName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)!
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var curSubs = Directory.Exists(_work.SubsDir)
                ? Directory.EnumerateFiles(_work.SubsDir, "*.srt").Select(Path.GetFileName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)!
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var v in curVideos)
                if (!log.Rename.Videos.Any(x => x.Renamed.Equals(v, StringComparison.OrdinalIgnoreCase)))
                    r.Added.RenamedVideos.Add(v!);

            foreach (var s in curSubs)
                if (!log.Rename.Subtitles.Any(x => x.Renamed.Equals(s, StringComparison.OrdinalIgnoreCase)))
                    r.Added.RenamedSubtitles.Add(s!);

            // --- Norm files
            var normDir = Path.Combine(_work.Root, "norm");
            foreach (var n in log.Normalize.Select(x => x.Norm))
                if (!File.Exists(Path.Combine(normDir, n))) r.Missing.Norm.Add(n);

            // --- Kết quả parts (MKV, SRT, Timeline đều nằm ở work root)
            foreach (var p in log.Parts)
            {
                if (!File.Exists(Path.Combine(_work.Root, p.Mkv))) r.Missing.MkvParts.Add(p.PartIndex);
                if (!File.Exists(Path.Combine(_work.Root, p.Srt))) r.Missing.SrtParts.Add(p.PartIndex);
                if (!File.Exists(Path.Combine(_work.Root, p.Timeline))) r.Missing.TimelineParts.Add(p.PartIndex);
            }

            return r;
        }
    }
}