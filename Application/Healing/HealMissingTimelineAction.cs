using Domain.Entities;
using Domain.Interfaces;
using Domain.Models;
using Models;

namespace Application.Healing
{
    public sealed class HealMissingTimelineAction : IHealingAction
    {
        public string Name => "2.5/2.10 - Rebuild missing timeline files";
        private readonly ITimelineWriter _timeline;
        private readonly IFileRenamer _renamer;
        private readonly IProcessingLogService _procLog;

        public HealMissingTimelineAction(ITimelineWriter t, IFileRenamer r, IProcessingLogService pl)
        { _timeline = t; _renamer = r; _procLog = pl; }

        public bool ShouldRun(ValidationResult v, ProcessingLog log)
            => v.Missing.TimelineParts.Count > 0;

        public Task RunAsync(string parentFolder, WorkDirs work, ProcessingLog log)
        {
            var all = _renamer.RenameAll(parentFolder).ToList();

            foreach (var p in log.Parts)
            {
                var tlPath = Path.Combine(work.Root, p.Timeline);
                if (File.Exists(tlPath)) continue;

                var clips = p.NormFiles.Select(n =>
                {
                    var stem = Path.GetFileNameWithoutExtension(n)!;
                    var renamed = stem.EndsWith(".norm", StringComparison.OrdinalIgnoreCase) ? stem[..^5] : stem;
                    return all.First(v => Path.GetFileNameWithoutExtension(v.RenamedName)!
                        .Equals(renamed, StringComparison.OrdinalIgnoreCase));
                }).ToList();

                var vp = new VideoPart(p.PartIndex, Path.Combine(work.Root, p.Mkv), clips);
                _timeline.WriteTimeline(vp, work.Root);
            }

            _procLog.Save(log);
            return Task.CompletedTask;
        }
    }
}
