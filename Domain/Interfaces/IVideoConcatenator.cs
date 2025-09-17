using Domain.Entities;

namespace Domain.Interfaces
{
    public interface IVideoConcatenator
    {
        Task<IList<VideoPart>> ConcatenateAsync(IEnumerable<VideoFile> videos, bool useGpuReencode);
    }
}
