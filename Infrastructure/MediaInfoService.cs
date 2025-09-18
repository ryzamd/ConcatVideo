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
            var psi = new ProcessStartInfo
            {
                FileName = _config.FfprobePath,
                Arguments = $"-v error -show_entries format=duration -of default=nk=1:nw=1 \"{fullPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = ProcessManager.Start(psi);
            var so = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (double.TryParse(so.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var sec)) return sec;
            return 0;
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
