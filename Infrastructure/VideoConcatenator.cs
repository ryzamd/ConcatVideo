using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Domain.Entities;
using Domain.Interfaces;
using Domain.Models;
using Models;
using Utilities;

namespace Infrastructure
{
    public sealed class VideoConcatenator : IVideoConcatenator
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
            Directory.CreateDirectory(_work.Root);
            var normDir = Path.Combine(_work.Root, "norm");
            Directory.CreateDirectory(normDir);

            var inputs = videos.Select(v => v.RenamedPath).ToList();
            if (inputs.Count == 0) throw new InvalidOperationException("No input files.");

            // 1) Normalize
            foreach (var src in inputs)
            {
                var srcName = Path.GetFileName(src);
                Console.Write($"  - Normalizing {srcName} -> ");
                var info = await ProbeAudioAsync(src).ConfigureAwait(false);
                var dst = Path.Combine(normDir, Path.GetFileNameWithoutExtension(src) + ".norm.mkv");

                var compliance = GetCompliance(info);
                if (compliance == AudioCompliance.Compliant)
                {
                    await RemuxCopyAsync(src, dst).ConfigureAwait(false);
                    Console.WriteLine("copy ✔");
                }
                else if (compliance == AudioCompliance.MissingAudio)
                {
                    await AddSilentAsync(src, dst).ConfigureAwait(false);
                    Console.WriteLine("add-silent+aac ✔");
                }
                else
                {
                    await EncodeAacAsync(src, dst, info).ConfigureAwait(false);
                    Console.WriteLine("aac ✔");
                }
            }

            // 2) Gom nhóm ≤ 2h30
            var normFiles = Directory.EnumerateFiles(normDir, "*.norm.mkv")
                                     .OrderBy(n => n, new NumericNameComparer())
                                     .ToList();

            const double maxHours = 2.5;
            var partsRaw = BuildGroupsByDuration(normFiles, maxHours);

            var courseFolderName = new DirectoryInfo(_work.Root).Parent?.Name ?? "Output";
            courseFolderName = Utils.SanitizeFileName(courseFolderName);

            // 3) Concat từng part
            var result = new List<VideoPart>();
            int idx = 1;
            foreach (var partInputs in partsRaw)
            {
                var outName = $"{courseFolderName} - [Part {idx:00}].mkv";
                var outPath = Path.Combine(_work.Root, outName);

                Console.WriteLine($"  - Concatenate [Part {idx:00}] ({partInputs.Count} clips) -> {outName}");

                var listPath = Path.Combine(_work.Root, $"concat_{idx:00}.txt");
                await File.WriteAllLinesAsync(
                    listPath,
                    partInputs.Select(p => $"file '{p.Replace("'", "''")}'"),
                    new UTF8Encoding(false)
                ).ConfigureAwait(false);

                var videoCodecArg = useGpuReencode ? BuildGpuVideoArgs() : "-c:v copy";
                var audioArg = "-c:a copy";

                var args = $"-hide_banner -y -f concat -safe 0 -i \"{listPath}\" {videoCodecArg} {audioArg} " +
                           "-fflags +genpts -avoid_negative_ts make_zero -max_interleave_delta 0 " +
                           $"\"{outPath}\"";

                RunTool(_config.FfmpegPath, args, TimeSpan.FromHours(12));
                try { File.Delete(listPath); } catch { }

                var clipsInPart = partInputs.Select(pf =>
                {
                    var stem = Path.GetFileNameWithoutExtension(pf)!;
                    if (stem.EndsWith(".norm", StringComparison.OrdinalIgnoreCase))
                        stem = stem[..^5];
                    return videos.First(v =>
                        Path.GetFileNameWithoutExtension(v.RenamedName)!
                            .Equals(stem, StringComparison.OrdinalIgnoreCase));
                }).ToList();

                result.Add(new VideoPart(idx, outPath, clipsInPart));
                idx++;
            }

            return result;
        }

        // === NEW: NormalizeOnlyAsync (chỉ làm lại những clip thiếu) ===
        public Task NormalizeOnlyAsync(IEnumerable<VideoFile> videosToNormalize)
        {
            Directory.CreateDirectory(Path.Combine(_work.Root, "norm"));
            return Task.WhenAll(videosToNormalize.Select(async v =>
            {
                var src = v.RenamedPath;
                var info = await ProbeAudioAsync(src).ConfigureAwait(false);
                var dst = Path.Combine(_work.Root, "norm", Path.GetFileNameWithoutExtension(src) + ".norm.mkv");

                var compliance = GetCompliance(info);
                if (compliance == AudioCompliance.Compliant) await RemuxCopyAsync(src, dst);
                else if (compliance == AudioCompliance.MissingAudio) await AddSilentAsync(src, dst);
                else await EncodeAacAsync(src, dst, info);
            }));
        }

        // === NEW: ConcatPartsFromNormAsync (concat 1..n part từ danh sách norm có sẵn) ===
        public async Task<IList<VideoPart>> ConcatPartsFromNormAsync(IEnumerable<(int partIndex, IList<string> normFiles, string outMkvName)> specs, bool useGpuReencode, IEnumerable<VideoFile> allVideos)
        {
            var result = new List<VideoPart>();
            foreach (var (partIndex, normFiles, outName) in specs)
            {
                var listPath = Path.Combine(_work.Root, $"concat_{partIndex:00}.txt");
                await File.WriteAllLinesAsync(listPath, normFiles.Select(n => $"file '{Path.Combine(_work.Root, "norm", n).Replace("'", "''")}'"), new UTF8Encoding(false));
                var outPath = Path.Combine(_work.Root, outName);
                var videoCodecArg = useGpuReencode ? BuildGpuVideoArgs() : "-c:v copy";
                var args = $"-hide_banner -y -f concat -safe 0 -i \"{listPath}\" {videoCodecArg} -c:a copy -fflags +genpts -avoid_negative_ts make_zero -max_interleave_delta 0 \"{outPath}\"";
                RunTool(_config.FfmpegPath, args, TimeSpan.FromHours(12));
                try { File.Delete(listPath); } catch { }

                var clips = new List<VideoFile>();
                foreach (var n in normFiles)
                {
                    var stem = Path.GetFileNameWithoutExtension(n)!; // "001.norm"
                    var renamed = stem.EndsWith(".norm", StringComparison.OrdinalIgnoreCase) ? stem[..^5] : stem;
                    var vf = allVideos.First(v => Path.GetFileNameWithoutExtension(v.RenamedName)!.Equals(renamed, StringComparison.OrdinalIgnoreCase));
                    clips.Add(vf);
                }
                result.Add(new VideoPart(partIndex, outPath, clips));
            }
            return result;
        }

        // ===================== helpers =====================
        private enum AudioCompliance { Compliant, MissingAudio, Other }

        private AudioCompliance GetCompliance(AudioInfo info)
        {
            if (string.IsNullOrEmpty(info.Codec)) return AudioCompliance.MissingAudio;
            if (info.Codec.Equals("aac", StringComparison.OrdinalIgnoreCase) && info.Channels == 2 && info.BitRate > 0)
                return AudioCompliance.Compliant;
            return AudioCompliance.Other;
        }

        private Task RemuxCopyAsync(string src, string dst)
        {
            return Task.Run(() =>
            {
                if (File.Exists(dst)) File.Delete(dst);
                var args = $"-hide_banner -y -i \"{src}\" -c copy \"{dst}\"";
                RunTool(_config.FfmpegPath, args, TimeSpan.FromHours(1));
            });
        }

        private Task AddSilentAsync(string src, string dst)
        {
            return Task.Run(() =>
            {
                if (File.Exists(dst)) File.Delete(dst);
                var args = $"-hide_banner -y -i \"{src}\" -f lavfi -i anullsrc=cl=stereo:r=48000 -shortest -c:v copy -c:a aac -b:a 192k \"{dst}\"";
                RunTool(_config.FfmpegPath, args, TimeSpan.FromHours(1));
            });
        }

        private Task EncodeAacAsync(string src, string dst, AudioInfo _)
        {
            return Task.Run(() =>
            {
                if (File.Exists(dst)) File.Delete(dst);
                var args = $"-hide_banner -y -i \"{src}\" -c:v copy -c:a aac -b:a 192k \"{dst}\"";
                RunTool(_config.FfmpegPath, args, TimeSpan.FromHours(2));
            });
        }

        private async Task<AudioInfo> ProbeAudioAsync(string src)
        {
            return await Task.Run(() =>
            {
                var args = $"-v error -select_streams a:0 -show_entries stream=codec_name,channels,bit_rate -of default=nw=1 \"{src}\"";
                var (exit, stdout, _) = RunToolCapture(_config.FfprobePath, args, TimeSpan.FromSeconds(30));
                if (exit != 0 || string.IsNullOrWhiteSpace(stdout))
                    return new AudioInfo(null, 0, 0);

                var dict = stdout.Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Contains('='))
                    .Select(l => (k: l[..l.IndexOf('=')], v: l[(l.IndexOf('=') + 1)..]))
                    .ToDictionary(x => x.k, x => x.v, StringComparer.OrdinalIgnoreCase);

                dict.TryGetValue("codec_name", out var codec);
                int.TryParse(dict.GetValueOrDefault("channels"), out var ch);
                int.TryParse(dict.GetValueOrDefault("bit_rate"), out var br);
                return new AudioInfo(codec, ch, br);
            }).ConfigureAwait(false);
        }

        private List<List<string>> BuildGroupsByDuration(List<string> files, double maxHours)
        {
            var maxSec = maxHours * 3600.0;
            var groups = new List<List<string>>();
            var cur = new List<string>();
            double acc = 0;

            foreach (var f in files)
            {
                var dur = GetDurationSec(f);
                if (cur.Count > 0 && (acc + dur) > maxSec)
                {
                    groups.Add(cur);
                    cur = new List<string>();
                    acc = 0;
                }
                cur.Add(f);
                acc += dur;
            }
            if (cur.Count > 0) groups.Add(cur);
            return groups;
        }

        private double GetDurationSec(string file)
        {
            // ffprobe - show duration
            var (exit, stdout, _) = RunToolCapture(
                _config.FfprobePath,
                $"-v error -show_entries format=duration -of default=nk=1:nw=1 \"{file}\"",
                TimeSpan.FromSeconds(30));
            if (exit == 0 && double.TryParse(stdout.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var sec))
                return sec;
            return 0;
        }

        private static string BuildGpuVideoArgs()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "-c:v h264_vaapi -vf format=nv12,hwupload";
            // Windows / NVIDIA
            return "-c:v h264_nvenc -preset p4 -rc:v vbr -cq:v 23";
        }

        private static void RunTool(string fileName, string args, TimeSpan timeout)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = ProcessManager.Start(psi);

            if (!p.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { p.Kill(true); } catch { }
                throw new TimeoutException($"{fileName} timed out.");
            }
        }

        private static (int Exit, string StdOut, string StdErr) RunToolCapture(string fileName, string args, TimeSpan timeout)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = ProcessManager.Start(psi);
            var so = p.StandardOutput.ReadToEnd();
            var se = p.StandardError.ReadToEnd();

            if (!p.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { p.Kill(true); } catch { }
                throw new TimeoutException($"{fileName} timed out.");
            }

            return (p.ExitCode, so, se);
        }
    }
}
