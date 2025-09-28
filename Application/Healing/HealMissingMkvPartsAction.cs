// Application/HealMissingMkvPartsAction.cs
using System.Diagnostics;
using System.Text;
using Domain.Interfaces;
using Domain.Models;
using Models;
using Infrastructure;

namespace Application;

public sealed class HealMissingMkvPartsAction : IHealingAction
{
    public string Name => "2.3/2.8 - Rebuild missing MKV parts";

    private readonly Config _config;
    private readonly IErrorLogger _logger;

    public HealMissingMkvPartsAction(Config config, IErrorLogger logger)
    {
        _config = config;
        _logger = logger;
    }

    // Chỉ chạy khi Validation phát hiện thiếu MKV (không cần re-scan)
    public bool ShouldRun(ValidationResult v, ProcessingLog log)
        => v.Missing.MkvParts.Count > 0;

    public async Task RunAsync(string parentFolder, WorkDirs work, ProcessingLog log)
    {
        var normDir = Path.Combine(work.Root, "norm");
        Directory.CreateDirectory(normDir);

        // Xác định chính xác những part còn thiếu theo log + thực tế file
        var missingParts = log.Parts
            .Where(p => !File.Exists(Path.Combine(work.Root, p.Mkv)))
            .OrderBy(p => p.PartIndex)
            .ToList();

        foreach (var part in missingParts)
        {
            // Tạo concat list từ NormFiles trong log (KHÔNG gọi rename/normalize)
            var concatList = Path.Combine(work.Root, $"concat_{part.PartIndex:00}.txt");
            var lines = new List<string>();
            foreach (var nf in part.NormFiles)
            {
                var full = Path.Combine(normDir, nf); // nf đã là *.norm.mkv
                lines.Add($"file '{full.Replace("'", "''")}'");
            }
            Directory.CreateDirectory(work.Root);
            await File.WriteAllLinesAsync(concatList, lines, new UTF8Encoding(false));

            var outPath = Path.Combine(work.Root, part.Mkv);

            // FFmpeg concat stream-copy (nhanh, không re-encode)
            var ffmpeg = _config.FfmpegPath;
            var args = $"-hide_banner -y -f concat -safe 0 -i \"{concatList}\" -c:v copy -c:a copy " +
                       "-async 1 -vsync cfr -fflags +genpts -avoid_negative_ts make_zero " +
                       "-max_interleave_delta 0 -max_muxing_queue_size 9999 " +
                       $"\"{outPath}\"";

            RunTool(ffmpeg, args, TimeSpan.FromHours(6));

            try { File.Delete(concatList); } catch { }
        }
    }

    private static void RunTool(string fileName, string args, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = ProcessManager.Start(psi);

        if (!p.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { p.Kill(true); } catch { }
            throw new TimeoutException($"{fileName} timed out.");
        }

        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} failed with exit code {p.ExitCode}");
        }
    }
}