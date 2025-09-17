using Domain.Entities;

namespace Domain.Interfaces
{
    public interface ISubtitleMapper
    {
        Dictionary<string, string> MapSubtitles(string subsDir, IEnumerable<VideoFile> videos);
    }
}