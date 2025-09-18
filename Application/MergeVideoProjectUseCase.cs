using Application.Healing;
using Domain.Entities;
using Domain.Interfaces;
using Domain.Models;
using Models;

namespace Application
{
    public class MergeVideoProjectUseCase
    {
        private readonly IFileRenamer _renamer;
        private readonly ISubtitleMapper _subMapper;
        private readonly IVideoConcatenator _concat;
        private readonly ISubtitleMerger _subs;
        private readonly ITimelineWriter _timeline;
        private readonly IErrorLogger _log;
        private readonly IExcelLogger _excel;
        private readonly IMediaInfoService _media;
        private readonly IProcessingLogService _procLog;

        private readonly Config _config;
        private readonly RuntimeOptions _opts;
        
        private readonly List<IHealingAction> _actions;

        public MergeVideoProjectUseCase(
            IFileRenamer renamer,
            ISubtitleMapper subMapper,
            IVideoConcatenator concatenator,
            ISubtitleMerger subMerger,
            ITimelineWriter timelineWriter,
            IErrorLogger errorLogger,
            IExcelLogger excelLogger,
            Config config,
            RuntimeOptions opts,
            IMediaInfoService media,
            IProcessingLogService procLog)
        {
            _renamer = renamer; _subMapper = subMapper; _concat = concatenator; _subs = subMerger; _timeline = timelineWriter;
            _log = errorLogger; _excel = excelLogger; _config = config; _opts = opts; _media = media; _procLog = procLog;

            _actions = new()
            {
                new HealMissingNormAction(_renamer, _concat, _media, _procLog),
                new HealMissingRenamedSubtitlesAction(_renamer, _media, _procLog),
                new HealMissingMkvPartsAction(_config, _log),
                new HealMissingSrtPartsAction(_renamer, _subMapper, _subs, _procLog),
                new HealMissingTimelineAction(_timeline, _renamer, _procLog),
                new HealNewVideosAppendAction(_renamer, _concat, _media, _subMapper, _subs, _timeline, _procLog) // A: bù thêm
            };
        }

        public async Task<int> ExecuteAsync(string parentFolder)
        {
            var work = WorkDirs.Prepare(parentFolder);

            // if work/ exists but missing processing.json -> purge & rebuild full
            if (Directory.Exists(work.Root) && !_procLog.Exists())
            {
                TryDelete(work.Root);
                Directory.CreateDirectory(work.Root);
                work = WorkDirs.Prepare(parentFolder);
            }

            if (_procLog.Exists())
            {
                var log = _procLog.Load();
                var val = _procLog.Validate(log);

                if (val.IsEverythingPresent)
                {
                    Console.Write("Folders and files are exist. Do you want to run again [Y/N]: ");
                    var inp = (Console.ReadLine() ?? "").Trim();
                    if (inp.Equals("Y", StringComparison.OrdinalIgnoreCase))
                    {
                        TryDelete(work.Root);
                        Directory.CreateDirectory(work.Root);
                        work = WorkDirs.Prepare(parentFolder);
                    }
                    else return 0;
                }
                else
                {
                    foreach (var a in _actions)
                    {
                        if (a.ShouldRun(val, log))
                        {
                            Console.WriteLine($"> Healing: {a.Name}");
                            await a.RunAsync(parentFolder, work, log);
                        }
                    }
                    return 0;
                }
            }

            // [1/4] Scan & rename
            Console.WriteLine();
            Console.WriteLine("[1/4] Renaming videos and subtitles..");
            var clips = _renamer.RenameAll(parentFolder).ToList();
            WriteCompletedBar();

            // build processing.json (rename + checksum)
            var proc = new ProcessingLog
            {
                Meta = new ProcessingLog.MetaInfo
                {
                    CourseName = new DirectoryInfo(parentFolder).Name,
                    CreatedAtUtc = DateTime.UtcNow,
                    Version = 1
                },
                Rename = new ProcessingLog.RenameInfo
                {
                    Videos = clips.Select(c => new ProcessingLog.RenameInfo.RItem
                    {
                        Original = c.OriginalName,
                        Renamed = c.RenamedName,
                        Sha1 = _media.ComputeSha1(c.RenamedPath)
                    }).ToList(),
                    Subtitles = Directory.EnumerateFiles(work.SubsDir, "*.srt")
                        .OrderBy(x => x).Select((p, i) => new ProcessingLog.RenameInfo.RItem
                        {
                            Index = i + 1,
                            Original = "(mapped)",
                            Renamed = Path.GetFileName(p)!,
                            Sha1 = _media.ComputeSha1(p)
                        }).ToList()
                }
            };
            _procLog.Save(proc);

            // [2/4] Normalize & concat
            Console.WriteLine();
            Console.WriteLine("[2/4] Normalizing and concatening videos..");
            var parts = (await _concat.ConcatenateAsync(clips, _opts.ReencodeVideoWithGpu)).ToList();
            WriteCompletedBar();

            // update Normalize list (duration + checksum)
            var normDir = Path.Combine(work.Root, "norm");
            proc.Normalize = Directory.EnumerateFiles(normDir, "*.norm.mkv").OrderBy(x => x, new NumericNameComparer())
                .Select(n => new ProcessingLog.NormalizeEntry
                {
                    RenamedVideo = Path.GetFileNameWithoutExtension(n)!.Replace(".norm", "") + ".mp4",
                    Norm = Path.GetFileName(n)!,
                    DurationSec = _media.GetDurationSec(n),
                    Action = "aac",
                    Sha1 = _media.ComputeSha1(n)
                }).ToList();

            // update Parts
            var courseName = new DirectoryInfo(work.Root).Parent?.Name ?? "Output";
            proc.Parts = parts.Select(p => new ProcessingLog.PartEntry
            {
                PartIndex = p.PartIndex,
                NormFiles = (p.Clips ?? new List<VideoFile>())
                    .Select(c => $"{Path.GetFileNameWithoutExtension(c.RenamedName)!}.norm.mkv")
                    .ToList(),
                Mkv = Path.GetFileName(p.OutputPath)!,
                Srt = Path.ChangeExtension(Path.GetFileName(p.OutputPath)!, ".srt")!,
                Timeline = $"{courseName} - [Part {p.PartIndex:00}]-timeline.txt"
            }).ToList();
            _procLog.Save(proc);

            // [3/4] Merge subtitles + timeline
            Console.WriteLine();
            Console.WriteLine("[3/4] Merging subtitles and writing timelines..");
            var subMap = _subMapper.MapSubtitles(work.SubsDir, clips);
            foreach (var p in parts)
            {
                var srtPath = Path.ChangeExtension(p.OutputPath, ".srt");
                _subs.MergeSubtitles(p, subMap, srtPath);
                _timeline.WriteTimeline(p, work.Root);
                Console.WriteLine($"  - Merged subtitles for [Part {p.PartIndex:00}] -> {srtPath}");
            }
            WriteCompletedBar();

            // [4/4] Finalize
            Console.WriteLine();
            Console.WriteLine("[4/4] Finalize outputs:");
            Console.WriteLine("Outputs in: " + work.Root);
            foreach (var p in parts) Console.WriteLine(" - Part video: " + p.OutputPath);
            foreach (var p in parts) Console.WriteLine(" - Subtitle: " + Path.ChangeExtension(p.OutputPath, ".srt"));

            TryDelete(work.VideosDir);
            Console.WriteLine();
            Console.WriteLine(" - Cleaned up: " + work.VideosDir);

            _excel.FlushAndSave();
            WriteCompletedBar();
            return 0;
        }

        private static void TryDelete(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }

        private static void WriteCompletedBar()
        {
            var bar = new string('━', 46);
            Console.WriteLine();
            Console.WriteLine($" {bar} 100%  ");
            Console.WriteLine();
        }
    }
}
