namespace Domain.Interfaces
{
    public interface IMediaInfoService
    {
        double GetDurationSec(string fullPath);     // ffprobe
        string ComputeSha1(string fullPath);        // checksum
    }
}
