using Domain.Entities;

namespace Domain.Interfaces
{
    public interface IVideoConcatenator
    {
        Task<IList<VideoPart>> ConcatenateAsync(IEnumerable<VideoFile> videos, bool useGpuReencode);

        Task NormalizeOnlyAsync(IEnumerable<VideoFile> videosToNormalize);

        Task<IList<VideoPart>> ConcatPartsFromNormAsync(IEnumerable<(int partIndex, IList<string> normFiles, string outMkvName)> specs, bool useGpuReencode, IEnumerable<VideoFile> allVideos);
    }
}
