using Domain.Interfaces;
using Domain.Models;
using Models;

namespace Application.Healing
{
    public sealed class HealMissingRenamedSubtitlesAction : IHealingAction
    {
        public string Name => "2.2/2.7 - Rebuild missing renamed subtitles";
        private readonly IFileRenamer _renamer;
        private readonly IMediaInfoService _media;
        private readonly IProcessingLogService _procLog;

        public HealMissingRenamedSubtitlesAction(IFileRenamer r, IMediaInfoService m, IProcessingLogService pl)
        { _renamer = r; _media = m; _procLog = pl; }

        public bool ShouldRun(ValidationResult v, ProcessingLog log)
            => v.Missing.RenamedSubtitles.Count > 0;

        public Task RunAsync(string parentFolder, WorkDirs work, ProcessingLog log)
        {
            // chạy lại rename cho toàn bộ để đảm bảo mapping; rẻ so với concat
            _renamer.RenameAll(parentFolder);

            // update log.Rename.Subtitles + checksum
            log.Rename.Subtitles = Directory.EnumerateFiles(work.SubsDir, "*.srt")
                .OrderBy(x => x)
                .Select((p, i) => new ProcessingLog.RenameInfo.RItem
                {
                    Index = i + 1,
                    Original = "(mapped)",
                    Renamed = Path.GetFileName(p)!,
                    Sha1 = _media.ComputeSha1(p)
                }).ToList();
            _procLog.Save(log);
            return Task.CompletedTask;
        }
    }
}
