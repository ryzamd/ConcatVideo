using Domain.Interfaces;
using Models;

namespace MergeVideo.Application
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
        private readonly Config _config;
        private readonly RuntimeOptions _opts;

        public MergeVideoProjectUseCase(
            IFileRenamer renamer,
            ISubtitleMapper subMapper,
            IVideoConcatenator concatenator,
            ISubtitleMerger subMerger,
            ITimelineWriter timelineWriter,
            IErrorLogger errorLogger,
            IExcelLogger excelLogger,
            Config config,
            RuntimeOptions opts)
        {
            _renamer = renamer;
            _subMapper = subMapper;
            _concat = concatenator;
            _subs = subMerger;
            _timeline = timelineWriter;
            _log = errorLogger;
            _excel = excelLogger;
            _config = config;
            _opts = opts;
        }

        public async Task<int> ExecuteAsync(string parentFolder)
        {
            var work = WorkDirs.Prepare(parentFolder);

            // ===================== [1/4] Scan & Rename =====================
            var clips = _renamer.RenameAll(parentFolder).ToList();
            WriteCompletedBar(); // ━━━━━ 100%

            if (clips.Count == 0)
            {
                _log.Error("No videos found.");
                return 2;
            }

            // Validate subtitle mapping (không vẽ bar riêng)
            var subMap = _subMapper.MapSubtitles(work.SubsDir, clips);

            // ===================== [2/4] Normalize & Concat =================
            Console.WriteLine();
            Console.WriteLine("[2/4] Normalize & concatenate videos...");
            var parts = (await _concat.ConcatenateAsync(clips, _opts.ReencodeVideoWithGpu)).ToList();
            WriteCompletedBar();

            // ===================== [3/4] Merge Subtitles + Timeline =========
            Console.WriteLine();
            Console.WriteLine("[3/4] Merging subtitles and writing timelines..");

            foreach (var p in parts)
            {
                var srtPath = Path.ChangeExtension(p.OutputPath, ".srt");
                _subs.MergeSubtitles(p, subMap, srtPath);
                _timeline.WriteTimeline(p, work.Root);
                Console.WriteLine($"  - Merged subtitles for [Part {p.PartIndex:00}] -> {srtPath}");
            }
            WriteCompletedBar();

            // ===================== [4/4] Finalize ===========================
            Console.WriteLine();
            Console.WriteLine("[4/4] Finalize outputs:");
            Console.WriteLine("Outputs in: " + work.Root);
            foreach (var part in parts)
                Console.WriteLine(" - Part video: " + part.OutputPath);
            foreach (var part in parts)
            {
                var srt = Path.ChangeExtension(part.OutputPath, ".srt");
                if (File.Exists(srt))
                    Console.WriteLine(" - Subtitle: " + srt);
            }

            try
            {
                if (Directory.Exists(work.VideosDir))
                {
                    Directory.Delete(work.VideosDir, true);
                    Console.WriteLine();
                    Console.WriteLine(" - Cleaned up: " + work.VideosDir);
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"Failed to cleanup videos folder: {ex.Message}");
            }

            _excel.FlushAndSave();
            WriteCompletedBar();

            return 0;
        }

        private static void WriteCompletedBar()
        {
            var bar = new string('━', 46);
            Console.WriteLine();
            Console.WriteLine($" {bar} 100%  ");
            Console.WriteLine();
        }
    }
}
