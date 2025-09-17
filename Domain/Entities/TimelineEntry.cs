namespace Domain.Entities
{
    public class TimelineEntry
    {
        public TimeSpan StartTime { get; }
        public string ClipOriginalName { get; }

        public TimelineEntry(TimeSpan startTime, string clipOriginalName)
        {
            StartTime = startTime;
            ClipOriginalName = clipOriginalName;
        }
    }
}