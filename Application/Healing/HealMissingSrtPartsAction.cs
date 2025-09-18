using Domain.Entities;
using Domain.Interfaces;
using Domain.Models;
using Models;

namespace Application.Healing
{
    public sealed class HealMissingSrtPartsAction : IHealingAction
    {
        public string Name => "2.4/2.9 - Rebuild missing SRT parts";
        private readonly IFileRenamer _renamer;
        private readonly ISubtitleMapper _map;
        private readonly ISubtitleMerger _merge;
        private readonly IProcessingLogService _procLog;

        public HealMissingSrtPartsAction(IFileRenamer r, ISubtitleMapper m, ISubtitleMerger s, IProcessingLogService pl)
        { _renamer = r; _map = m; _merge = s; _procLog = pl; }

        public bool ShouldRun(ValidationResult v, ProcessingLog log)
            => v.Missing.SrtParts.Count > 0;

        public Task RunAsync(string parentFolder, WorkDirs work, ProcessingLog log)
        {
            var all = _renamer.RenameAll(parentFolder).ToList();
            var subMap = _map.MapSubtitles(work.SubsDir, all);
            foreach (var idx in log.Parts.Where(p => !File.Exists(Path.Combine(work.Root, p.Srt))).Select(p => p.PartIndex))
            {
                var part = log.Parts.First(p => p.PartIndex == idx);
                var clips = part.NormFiles.Select(n =>
                {
                    var stem = Path.GetFileNameWithoutExtension(n)!;
                    var renamed = stem.EndsWith(".norm") ? stem[..^5] : stem;
                    return all.First(v => Path.GetFileNameWithoutExtension(v.RenamedName)!.Equals(renamed, System.StringComparison.OrdinalIgnoreCase));
                }).ToList();

                var vp = new VideoPart(part.PartIndex, Path.Combine(work.Root, part.Mkv), clips);
                _merge.MergeSubtitles(vp, subMap, Path.Combine(work.Root, part.Srt));
            }
            _procLog.Save(log);
            return Task.CompletedTask;
        }
    }
}
