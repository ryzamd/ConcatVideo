using Domain.Entities;

namespace Domain.Interfaces
{
    public interface ISubtitleMerger
    {
        void MergeSubtitles(VideoPart part, Dictionary<string, string> baseToSubtitlePath, string outputSrtPath);
    }
}