// Application/HealMissingMkvPartsAction.cs
using System.Diagnostics;
using System.Text;
using Domain.Interfaces;
using Domain.Models;
using Models;

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
            var args = $"-y -f concat -safe 0 -i \"{concatList}\" -c copy \"{outPath}\"";

            var ok = await RunFfmpegAsync(ffmpeg, args, killOnCtrlC: true);
            if (!ok)
            {
                _logger.Warn($"Failed to rebuild MKV for Part {part.PartIndex:00}");
            }
        }
    }

    private static async Task<bool> RunFfmpegAsync(string exe, string args, bool killOnCtrlC)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Đọc output để tránh deadlock
        p.OutputDataReceived += (_, __) => { };
        p.ErrorDataReceived += (_, __) => { };

        ConsoleCancelEventHandler? onCancel = null;
        if (killOnCtrlC)
        {
            onCancel = (_, __) =>
            {
                try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
            };
            Console.CancelKeyPress += onCancel;
        }

        try
        {
            if (!p.Start()) return false;
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.Exited += (_, __) => tcs.TrySetResult(p.ExitCode == 0);
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            if (onCancel != null) Console.CancelKeyPress -= onCancel;
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
        }
    }
}
