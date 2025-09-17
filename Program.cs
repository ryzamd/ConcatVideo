using Domain.Interfaces;

namespace MergeVideoSuper
{
    internal static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            try
            {
                string root = args.Length > 0 ? Path.GetFullPath(args[0]) : GetRootFromInput();
                if (root == null) return 0;

                var opts = new RuntimeOptions();
                // Example: set opts.ReencodeVideoWithGpu = true if needed
                var cfg = Config.DefaultWithToolPaths();
                var work = WorkDirs.Prepare(root);
                var errorLogger = new ErrorLogger(work.ReportDir);
                using var excelLogger = new ExcelLogger(work.LogsDir);

                // Instantiate services
                IFileRenamer renamer = new FileRenamer(cfg, work, errorLogger, excelLogger, opts);
                ISubtitleMapper subMapper = new SubtitleMapper(cfg, work, opts, errorLogger);
                IVideoConcatenator concatenator = new VideoConcatenator(cfg, work, errorLogger, opts);
                ISubtitleMerger subMerger = new SubtitleMerger(cfg, errorLogger);
                ITimelineWriter timelineWriter = new TimelineWriter();

                var useCase = new MergeVideoProjectUseCase(
                    renamer, subMapper, concatenator, subMerger, timelineWriter,
                    errorLogger, excelLogger, cfg, opts);

                return useCase.ExecuteAsync(root).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine("FATAL: " + ex);
                return 99;
            }
        }

        private static string GetRootFromInput()
        {
            Console.Write("Root path: ");
            var line = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) return null;
            return Path.GetFullPath(line);
        }
    }
}
