using Domain.Interfaces;
using Domain.Models;
using Models;
    
namespace Application.Healing
{
    public sealed class HealMissingNormAction : IHealingAction
    {
        public string Name => "2.1/2.6 - Rebuild missing norm files";
        private readonly IFileRenamer _renamer;
        private readonly IVideoConcatenator _concat;
        private readonly IMediaInfoService _media;
        private readonly IProcessingLogService _procLog;

        public HealMissingNormAction(IFileRenamer r, IVideoConcatenator c, IMediaInfoService m, IProcessingLogService pl)
        { _renamer = r; _concat = c; _media = m; _procLog = pl; }

        public bool ShouldRun(ValidationResult v, ProcessingLog log) => v.Missing.Norm.Count > 0;

        public async Task RunAsync(string parentFolder, WorkDirs work, ProcessingLog log)
        {
            var all = _renamer.RenameAll(parentFolder).ToList();
            var need = all.Where(v =>
                v != null && (
                    v.RenamedName != null &&
                    (log.Normalize.Any(n => n.RenamedVideo.Equals(v.RenamedName, System.StringComparison.OrdinalIgnoreCase) &&
                                            !File.Exists(Path.Combine(work.Root, "norm", n.Norm))) ||
                     !File.Exists(Path.Combine(work.VideosDir, v.RenamedName)))
                ));

            await _concat.NormalizeOnlyAsync(need);

            // update log.Normalize (duration + sha1)
            foreach (var n in Directory.EnumerateFiles(Path.Combine(work.Root, "norm"), "*.norm.mkv"))
            {
                var name = Path.GetFileName(n)!;
                var e = log.Normalize.FirstOrDefault(x => x.Norm.Equals(name, System.StringComparison.OrdinalIgnoreCase));
                if (e == null)
                {
                    var stem = Path.GetFileNameWithoutExtension(name)!.Replace(".norm", "");
                    log.Normalize.Add(new ProcessingLog.NormalizeEntry
                    {
                        RenamedVideo = stem + ".mp4",
                        Norm = name,
                        DurationSec = _media.GetDurationSec(n),
                        Action = "unknown",
                        Sha1 = _media.ComputeSha1(n)
                    });
                }
                else
                {
                    e.DurationSec = _media.GetDurationSec(n);
                    e.Sha1 = _media.ComputeSha1(n);
                }
            }
            _procLog.Save(log);

            if (Directory.Exists(work.VideosDir))
            {
                try { Directory.Delete(work.VideosDir, recursive: true); }
                catch { /* ignore */ }
            }
        }
    }
}
