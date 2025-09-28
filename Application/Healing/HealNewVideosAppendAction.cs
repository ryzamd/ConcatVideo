using Domain.Entities;
using Domain.Interfaces;
using Domain.Models;
using Models;

namespace Application.Healing
{
    public sealed class HealNewVideosAppendAction : IHealingAction
    {
        public string Name => "[Extention]: Append new videos to last part (<=2h30), overflow => new parts";

        private readonly IFileRenamer _renamer;
        private readonly IVideoConcatenator _concat;
        private readonly IMediaInfoService _media;
        private readonly ISubtitleMapper _map;
        private readonly ISubtitleMerger _merge;
        private readonly ITimelineWriter _timeline;
        private readonly IProcessingLogService _procLog;

        public HealNewVideosAppendAction(
            IFileRenamer r, IVideoConcatenator c, IMediaInfoService m,
            ISubtitleMapper map, ISubtitleMerger merge, ITimelineWriter timeline, IProcessingLogService pl)
        {
            _renamer = r; _concat = c; _media = m; _map = map; _merge = merge; _timeline = timeline; _procLog = pl;
        }

        public bool ShouldRun(ValidationResult v, ProcessingLog log)
            => v.Added.RenamedVideos.Count > 0; // chỉ care video mới

        public async Task RunAsync(string parentFolder, WorkDirs work, ProcessingLog log)
        {
            var all = _renamer.RenameAll(parentFolder).OrderBy(v => v.RenamedName).ToList();
            var newVideos = all.Where(v => !log.Rename.Videos.Any(x => x.Renamed.Equals(v.RenamedName, StringComparison.OrdinalIgnoreCase))).ToList();
            if (newVideos.Count == 0) return;

            // 1) normalize cho video mới
            await _concat.NormalizeOnlyAsync(newVideos);

            // 2) cập nhật log.Rename + Normalize (checksum + duration)
            foreach (var v in newVideos)
            {
                log.Rename.Videos.Add(new ProcessingLog.RenameInfo.RItem
                {
                    Index = v.Index,
                    Original = v.OriginalName,
                    Renamed = v.RenamedName,
                    Sha1 = _media.ComputeSha1(v.RenamedPath)
                });
                var norm = Path.Combine(work.Root, "norm", Path.GetFileNameWithoutExtension(v.RenamedName) + ".norm.mkv");
                log.Normalize.Add(new ProcessingLog.NormalizeEntry
                {
                    RenamedVideo = v.RenamedName,
                    Norm = Path.GetFileName(norm)!,
                    DurationSec = _media.GetDurationSec(norm),
                    Action = "aac",
                    Sha1 = _media.ComputeSha1(norm)
                });
            }

            // 3) Bù thêm vào Part cuối
            const double MAX_SEC = 4.5 * 3600;
            var last = log.Parts.OrderBy(p => p.PartIndex).LastOrDefault();
            var course = new DirectoryInfo(work.Root).Parent?.Name ?? "Output";

            // Tính dung lượng còn trống của last part
            double used = 0;
            if (last != null)
                used = last.NormFiles.Sum(n => log.Normalize.FirstOrDefault(x => x.Norm.Equals(n, StringComparison.OrdinalIgnoreCase))?.DurationSec ?? 0);

            var queue = new Queue<ProcessingLog.NormalizeEntry>(
                newVideos
                .Select(v => Path.GetFileNameWithoutExtension(v.RenamedName) + ".norm.mkv")
                .Select(n => log.Normalize.First(x => x.Norm.Equals(n, StringComparison.OrdinalIgnoreCase)))
            );

            var specs = new List<(int partIndex, IList<string> normFiles, string outName)>();

            if (last == null)
            {
                last = new ProcessingLog.PartEntry { PartIndex = 1, NormFiles = new List<string>(), Mkv = $"{course} - [Part 01].mkv", Srt = $"{course} - [Part 01].srt", Timeline = $"{course} - [Part 01]-timeline.txt" };
                log.Parts.Add(last);
                used = 0;
            }

            // fill phần còn trống của last
            while (queue.Count > 0 && used < MAX_SEC)
            {
                var top = queue.Peek();
                if (used + top.DurationSec > MAX_SEC) break;
                queue.Dequeue();
                last.NormFiles.Add(top.Norm);
                used += top.DurationSec;
            }
            if (used > 0) specs.Add((last.PartIndex, last.NormFiles.ToList(), last.Mkv));

            // phần dư => tạo part mới
            var nextIdx = (last?.PartIndex ?? 0) + 1;
            while (queue.Count > 0)
            {
                var list = new List<string>();
                double acc = 0;
                while (queue.Count > 0 && acc + queue.Peek().DurationSec <= MAX_SEC)
                {
                    var x = queue.Dequeue();
                    list.Add(x.Norm);
                    acc += x.DurationSec;
                }
                var mkv = $"{course} - [Part {nextIdx:00}].mkv";
                var srt = $"{course} - [Part {nextIdx:00}].srt";
                var tl = $"{course} - [Part {nextIdx:00}]-timeline.txt";
                specs.Add((nextIdx, list, mkv));
                log.Parts.Add(new ProcessingLog.PartEntry { PartIndex = nextIdx, NormFiles = list, Mkv = mkv, Srt = srt, Timeline = tl });
                nextIdx++;
            }

            // 4) concat các part bị ảnh hưởng (last + new)
            var built = await _concat.ConcatPartsFromNormAsync(specs, false, all);

            // 5) merge SRT + timeline cho các part mới/updated
            var subMap = _map.MapSubtitles(work.SubsDir, all);
            foreach (var p in log.Parts.Where(pp => specs.Any(s => s.partIndex == pp.PartIndex)))
            {
                var vp = new VideoPart(p.PartIndex, Path.Combine(work.Root, p.Mkv),
                    p.NormFiles.Select(n =>
                    {
                        var rn = Path.GetFileNameWithoutExtension(n)!.Replace(".norm", "") + ".mp4";
                        return all.First(v => v.RenamedName.Equals(rn, StringComparison.OrdinalIgnoreCase));
                    }).ToList());
                _merge.MergeSubtitles(vp, subMap, Path.Combine(work.Root, p.Srt));
                _timeline.WriteTimeline(vp, work.Root);
            }

            _procLog.Save(log);
        }
    }
}
