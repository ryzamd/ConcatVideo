using Application;
using Domain.Interfaces;
using Infrastructure;
using Models;

namespace CLI
{
    public static class Program
    {
        static int Main(string[] args)
        {
            _ = typeof(ProcessManager);
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            try
            {
                // Nếu có tham số dòng lệnh, chạy 1 lần và thoát
                if (args.Length > 0)
                {
                    string? root = Path.GetFullPath(args[0]);
                    return ExecuteProcess(root);
                }

                while (true)
                {
                    string? root = GetRootFromInput();
                    if (root == null) continue;

                    if (IsQuitCommand(root))
                    {
                        Console.WriteLine("Exiting program...");
                        CleanupResources();
                        return 0;
                    }

                    try
                    {
                        int result = ExecuteProcess(root);

                        if (result == 0)
                        {
                            Console.WriteLine();
                            Console.WriteLine("=== Process completed successfully! ===");
                        }
                        else
                        {
                            Console.WriteLine($"=== Process completed with exit code: {result} ===");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ERROR during processing: {ex.Message}");
                        Console.WriteLine("You can try again with a different path or type 'q' to quit.");
                    }

                    Console.WriteLine();
                    Console.WriteLine("Press Enter to continue or type 'q' to quit...");
                    var continueInput = Console.ReadLine();
                    if (IsQuitCommand(continueInput))
                    {
                        Console.WriteLine("Exiting program...");
                        CleanupResources();
                        return 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("FATAL: " + ex);
                CleanupResources();
                return 99;
            }
        }

        private static int ExecuteProcess(string root)
        {
            var opts = new RuntimeOptions();
            // Example: set opts.ReencodeVideoWithGpu = true if needed
            var cfg = Config.DefaultWithToolPaths();
            var work = WorkDirs.Prepare(root);
            var errorLogger = new ErrorLogger(work.ReportDir);
            using var excelLogger = new ExcelLogger(work.LogsDir);

            IFileRenamer renamer = new FileRenamer(cfg, work, errorLogger, excelLogger, opts);
            ISubtitleMapper subMapper = new SubtitleMapper(cfg, work, opts, errorLogger);
            IVideoConcatenator concatenator = new VideoConcatenator(cfg, work, errorLogger, opts);
            ISubtitleMerger subMerger = new SubtitleMerger(cfg, errorLogger);
            ITimelineWriter timelineWriter = new TimelineWriter(cfg);
            IMediaInfoService mediaInfoService = new MediaInfoService(cfg);
            IProcessingLogService processingLogService = new ProcessingLogService(work, errorLogger);

            var useCase = new MergeVideoProjectUseCase(
                renamer, subMapper, concatenator, subMerger, timelineWriter,
                errorLogger, excelLogger, cfg, opts, mediaInfoService, processingLogService);

            return useCase.ExecuteAsync(root).GetAwaiter().GetResult();
        }

        private static string? GetRootFromInput()
        {
            Console.WriteLine();
            Console.Write("Root path (or 'q' to quit): ");
            var line = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(line)) return null;

            if (IsQuitCommand(line)) return line;

            try
            {
                return Path.GetFullPath(line);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Invalid path: {ex.Message}");
                return null;
            }
        }

        private static bool IsQuitCommand(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;

            var trimmed = input.Trim();
            return trimmed.Equals("q", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("Q", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("Quit", StringComparison.OrdinalIgnoreCase);
        }

        private static void CleanupResources()
        {
            try
            {
                ProcessManager.KillAll("Program Exit");
                Console.WriteLine("Resources cleaned up successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Error during cleanup: {ex.Message}");
            }
        }
    }
}