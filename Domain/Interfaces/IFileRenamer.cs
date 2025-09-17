using Domain.Entities;

namespace Domain.Interfaces
{
    public interface IFileRenamer
    {
        IEnumerable<VideoFile> RenameAll(string parentFolder);
    }
}
