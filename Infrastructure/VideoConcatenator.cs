// Infrastructure/VideoConcatenator.cs
using Domain.Entities;
using Domain.Interfaces;
using Domain.Models;
using Models;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Utilities;

namespace Infrastructure
{
    public class VideoConcatenator : IVideoConcatenator
    {
        private readonly Config _config;
        private readonly WorkDirs _work;
        private readonly IErrorLogger _logger;
        private readonly RuntimeOptions _opts;

        public VideoConcatenator(Config config, WorkDirs work, IErrorLogger logger, RuntimeOptions opts)
        {
            _config = config;
            _work = work;
            _logger = logger;
            _opts = opts;
        }

        public async Task<IList<VideoPart>> ConcatenateAsync(IEnumerable<VideoFile> videos, bool useGpuReencode)
        {
            // Prepare directories
            Directory.CreateDirectory(_work.Root);
            var normDir = Path.Combine(_work.Root, "norm");
            Directory.CreateDirectory(normDir);

            var inputs = videos.Select(v => v.RenamedPath).ToList();
            if (inputs.Count == 0) throw new InvalidOperationException("No input files.");

            // Normalize all videos (audio compliance)
            ConsoleProgressBar.WriteHeader("[2.1/4] Normalize all video ....");
            var normTasks = inputs.Select(src => Task.Run(async () =>
            {
                var info = await ProbeAudioAsync(src);
                var dst = Path.Combine(normDir, Path.GetFileNameWithoutExtension(src) + ".norm.mkv");
                var baseName = Path.GetFileName(src);
                switch (GetCompliance(info))
                {
                    case AudioCompliance.Compliant:
                        await RemuxCopyAsync(src, dst);
                        break;
                    case AudioCompliance.MissingAudio:
                        await AddSilentAsync(src, dst);
                        break;
                    default:
                        await EncodeAacAsync(src, dst, info);
                        break;
                }
                Console.WriteLine($"[Norm] {baseName} done.");
            }));
            await Task.WhenAll(normTasks);

            // Build list of normalized files in order
            var normFiles = Directory.EnumerateFiles(normDir, "*.norm.mkv")
                .OrderBy(n => n, new NumericNameComparer()).ToList();

            // Build parts with max 2.5h threshold
            double maxHours = 2.5;
            double maxSec = maxHours * 3600.0;
            var parts = new List<List<string>>();
            var cur = new List<string>();
            double curSec = 0;
            foreach (var f in normFiles)
            {
                var dur = GetDurationSafe(f);
                if (cur.Count > 0 && (curSec + dur) > maxSec)
                {
                    parts.Add(cur);
                    cur = new List<string>();
                    curSec = 0;
                }
                cur.Add(f);
                curSec += dur;
            }
            if (cur.Count > 0) parts.Add(cur);

            // Concatenate each part using ffmpeg concat demuxer
            ConsoleProgressBar.WriteHeader("[2.2/4] Concatenating video:");
            var videoParts = new List<VideoPart>();
            int idx = 1;
            foreach (var partInputs in parts)
            {
                string name = Path.GetFileNameWithoutExtension(_config.FfmpegPath); // use base output name
                name = _config.MkvMergePath; // placeholder, actually use baseName passed in opts
                // Build actual output name with part index (mimic original naming)
                var parentName = Utils.SanitizeFileName(Path.GetFileName(_work.Root.TrimEnd(Path.DirectorySeparatorChar)));
                var partName = $"{parentName} - Part {idx:00}.mkv";
                var outPath = Path.Combine(_work.Root, partName);

                // Create ffmpeg concat list file
                var listPath = Path.Combine(_work.Root, $"concat_{idx:00}.txt");
                using (var sw = new StreamWriter(listPath, false, new System.Text.UTF8Encoding(false)))
                {
                    foreach (var p in partInputs)
                    {
                        var safe = p.Replace("\\", "\\\\").Replace("'", "''");
                        sw.WriteLine($"file '{safe}'");
                    }
                }

                // Build ffmpeg args
                string videoCodecArg = useGpuReencode
                    ? (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "-c:v h264_vaapi -vf format=yuv420p" : "-c:v h264_nvenc -preset fast")
                    : "-c:v copy";
                var audioArg = "-c:a copy";
                var args = $"-f concat -safe 0 -i \"{listPath}\" {videoCodecArg} {audioArg} -fflags +genpts -avoid_negative_ts make_zero -max_interleave_delta 0 \"{outPath}\"";

                // Run with progress
                await ConsoleProgressBar.RunFfmpegWithProgressAsync(
                    _config.FfmpegPath,
                    args,
                    totalDurationSec: partInputs.Sum(GetDurationSafe),
                    workingDir: _work.Root,
                    onPercent: async p => { /* progress shown automatically */ }
                );

                try { File.Delete(listPath); } catch { }

                // Map inputs to domain VideoFile objects
                var partClips = partInputs.Select(pf =>
                {
                    var baseName = Path.GetFileNameWithoutExtension(pf).Replace(".norm", "");
                    return videos.First(v => Path.GetFileNameWithoutExtension(v.RenamedName) == baseName);
                }).ToList();

                videoParts.Add(new VideoPart(idx, outPath, partClips));

                idx++;
            }

            // If GPU re-encode: the above already used NVENC/VAAPI in ffmpeg args.
            // No further action needed, as encoding was done in concat step.

            return videoParts;
        }

        // Helper to run ffprobe and parse audio info
        private async Task<AudioInfo> ProbeAudioAsync(string src)
        {
            return await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _config.FfprobePath,
                    Arguments = $"-v error -select_streams a:0 -show_entries stream=codec_name,channels,bit_rate -of default=nw=1 \"{src}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi)!;
                var outStr = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                // Simple parse: if no output, missing audio.
                if (string.IsNullOrWhiteSpace(outStr))
                    return new AudioInfo { Codec = null, Channels = 0, BitRate = 0 };
                var lines = outStr.Split('\n').Select(l => l.Trim()).Where(l => l.Contains('=')).ToDictionary(
                    l => l.Substring(0, l.IndexOf('=')), l => l.Substring(l.IndexOf('=') + 1));
                lines.TryGetValue("codec_name", out var codec);
                int.TryParse(lines.GetValueOrDefault("channels"), out var ch);
                int.TryParse(lines.GetValueOrDefault("bit_rate"), out var br);
                return new AudioInfo { Codec = codec, Channels = ch, BitRate = br };
            });
        }

        private enum AudioCompliance { Compliant, MissingAudio, Other };
        private AudioCompliance GetCompliance(AudioInfo info)
        {
            if (string.IsNullOrEmpty(info.Codec)) return AudioCompliance.MissingAudio;
            if (info.Codec.Equals("aac", StringComparison.OrdinalIgnoreCase) && info.Channels == 2 && info.BitRate > 0)
                return AudioCompliance.Compliant;
            return AudioCompliance.Other;
        }
        private async Task RemuxCopyAsync(string src, string dst)
        {
            await Task.Run(() =>
            {
                File.Delete(dst);
                Utils.FfmpegConcatCopy(new[] { src }, dst, _config.FfmpegPath);
            });
        }
        private async Task AddSilentAsync(string src, string dst)
        {
            await Task.Run(() =>
            {
                File.Delete(dst);
                var args = $"-hide_banner -y -i \"{src}\" -f lavfi -i anullsrc=cl=stereo:r=48000 -shortest -c:v copy -c:a aac -b:a 192k \"{dst}\"";
                var psi = new ProcessStartInfo { FileName = _config.FfmpegPath, Arguments = args, UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi)!; p.WaitForExit();
            });
        }
        private async Task EncodeAacAsync(string src, string dst, AudioInfo info)
        {
            await Task.Run(() =>
            {
                File.Delete(dst);
                var args = $"-hide_banner -y -i \"{src}\" -c:v copy -c:a aac -b:a 192k \"{dst}\"";
                var psi = new ProcessStartInfo { FileName = _config.FfmpegPath, Arguments = args, UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi)!; p.WaitForExit();
            });
        }

        private double GetDurationSafe(string f)
        {
            try
            {
                var (exit, stdout, _) = RunToolCapture(_config.FfprobePath,
                    $"-v error -show_entries format=duration -of default=nk=1:nw=1 \"{f}\"");
                if (exit == 0 && double.TryParse(stdout.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var sec))
                    return sec;
            }
            catch { }
            return 0;
        }

        // Helper to run external process and capture output
        private static (int ExitCode, string StdOut, string StdErr) RunToolCapture(string fileName, string args)
        {
            var psi = new ProcessStartInfo(fileName, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var proc = Process.Start(psi)!;
            string outp = proc.StandardOutput.ReadToEnd();
            string err = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return (proc.ExitCode, outp, err);
        }
    }
}
