using Domain.Interfaces;
using Models;

namespace Application
{
    public class MergeVideoProjectUseCase
    {
        private readonly IFileRenamer _renamer;
        private readonly ISubtitleMapper _subMapper;
        private readonly IVideoConcatenator _concatenator;
        private readonly ISubtitleMerger _subMerger;
        private readonly ITimelineWriter _timelineWriter;
        private readonly IErrorLogger _errorLogger;
        private readonly IExcelLogger _excelLogger;
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
            _concatenator = concatenator;
            _subMerger = subMerger;
            _timelineWriter = timelineWriter;
            _errorLogger = errorLogger;
            _excelLogger = excelLogger;
            _config = config;
            _opts = opts;
        }

        public async Task<int> ExecuteAsync(string parentFolder)
        {
            // Prepare working directories
            var work = WorkDirs.Prepare(parentFolder);
            Console.WriteLine("[1/4] Scanning sub-folders & renaming files...");
            // Step 1: Rename files
            var videoClips = _renamer.RenameAll(parentFolder).ToList();
            Console.WriteLine("[green]########################### - 100%[/]");

            if (videoClips.Count == 0)
            {
                _errorLogger.Error("No videos found.");
                return 2;
            }

            // Step 2: Validate subtitle mapping
            var subMap = _subMapper.MapSubtitles(work.SubsDir, videoClips);

            // Step 3: Concatenate videos
            Console.WriteLine("[2/4] Concatenating videos ...");
            var parts = await _concatenator.ConcatenateAsync(videoClips, _opts.ReencodeVideoWithGpu);

            // Step 4: Write timeline and merge subtitles for each part
            Console.WriteLine("[3/4] Merging subtitles and writing timelines...");
            foreach (var part in parts)
            {
                // Merge subtitles for this part
                var srtPath = Path.ChangeExtension(part.OutputPath, ".srt");
                _subMerger.MergeSubtitles(part, subMap, srtPath);

                // Write timeline
                _timelineWriter.WriteTimeline(part, work.LogsDir);
            }

            // Done
            Console.WriteLine("[4/4] Done. Outputs in: " + work.Root);
            foreach (var part in parts)
                Console.WriteLine(" - Part video: " + part.OutputPath);
            foreach (var part in parts)
            {
                var srt = Path.ChangeExtension(part.OutputPath, ".srt");
                if (File.Exists(srt))
                    Console.WriteLine(" - Subtitle: " + srt);
            }

            // Cleanup: delete work/videos folder
            try
            {
                if (Directory.Exists(work.VideosDir))
                {
                    Directory.Delete(work.VideosDir, true);
                    Console.WriteLine("Cleaned up: " + work.VideosDir);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: failed to delete {work.VideosDir}: {ex.Message}");
            }

            // Save logs
            _excelLogger.FlushAndSave();
            return 0;
        }
    }
}
