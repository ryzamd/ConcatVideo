using Domain.Entities;

namespace Domain.Interfaces
{
    public interface ITimelineWriter
    {
        void WriteTimeline(VideoPart part, string outputDirectory);
    }
}
