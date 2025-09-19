using System.Diagnostics;
using System.Globalization;
using Domain.Interfaces;
using Models;

namespace Infrastructure
{
    public sealed class MediaInfoService : IMediaInfoService
    {
        private readonly Config _config;
        public MediaInfoService(Config cfg) { _config = cfg; }

        public double GetDurationSec(string fullPath)
        {
            var (exit, stdout, _) = RunToolCapture(
                _config.FfprobePath,
                $"-v error -show_entries format=duration -of default=nk=1:nw=1 \"{fullPath}\"",
                TimeSpan.FromSeconds(30));

            if (exit == 0 && double.TryParse(stdout.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var sec))
                return sec;
            return 0;
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

        public string ComputeSha1(string fullPath)
        {
            using var fs = File.OpenRead(fullPath);
            using var sha = System.Security.Cryptography.SHA1.Create();
            var hash = sha.ComputeHash(fs);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}