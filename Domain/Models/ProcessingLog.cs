namespace Domain.Models
{
    public sealed class ProcessingLog
    {
        public MetaInfo Meta { get; set; } = new();
        public RenameInfo Rename { get; set; } = new();
        public List<NormalizeEntry> Normalize { get; set; } = new();
        public List<PartEntry> Parts { get; set; } = new();

        public sealed class MetaInfo
        {
            public string CourseName { get; set; } = "";
            public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
            public int Version { get; set; } = 1;
        }

        public sealed class RenameInfo
        {
            public List<RItem> Videos { get; set; } = new();
            public List<RItem> Subtitles { get; set; } = new();

            public sealed class RItem
            {
                public int Index { get; set; }
                public string Original { get; set; } = "";
                public string Renamed { get; set; } = ""; // e.g., "001.mp4" / "001.srt"
                public string Sha1 { get; set; } = "";    // checksum file đã rename (trong work/)
            }
        }

        public sealed class NormalizeEntry
        {
            public string RenamedVideo { get; set; } = ""; // "001.mp4"
            public string Norm { get; set; } = "";         // "001.norm.mkv"
            public string Action { get; set; } = "";       // "copy|add-silent|aac"
            public double DurationSec { get; set; }
            public string Sha1 { get; set; } = "";         // checksum của norm.mkv
        }

        public sealed class PartEntry
        {
            public int PartIndex { get; set; }
            public List<string> NormFiles { get; set; } = new(); // ["001.norm.mkv", ...]
            public string Mkv { get; set; } = "";                 // "Course - [Part 01].mkv"
            public string Srt { get; set; } = "";                 // "Course - [Part 01].srt"
            public string Timeline { get; set; } = "";            // "Course - [Part 01]-timeline.txt"
        }
    }
}
