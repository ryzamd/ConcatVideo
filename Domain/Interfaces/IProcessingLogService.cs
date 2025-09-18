using Domain.Models;

namespace Domain.Interfaces
{
    public interface IProcessingLogService
    {
        bool Exists();
        ProcessingLog Load();
        void Save(ProcessingLog log);

        ValidationResult Validate(ProcessingLog log);
    }

    public sealed class ValidationResult
    {
        public bool WorkFolderExists { get; set; }
        public bool HasProcessingLog { get; set; }

        public MissingSet Missing { get; set; } = new();
        public AddedSet Added { get; set; } = new();

        public bool IsEverythingPresent =>
            Missing.RenamedVideos.Count == 0 &&
            Missing.RenamedSubtitles.Count == 0 &&
            Missing.Norm.Count == 0 &&
            Missing.MkvParts.Count == 0 &&
            Missing.SrtParts.Count == 0 &&
            Missing.TimelineParts.Count == 0;

        public sealed class MissingSet
        {
            public List<string> RenamedVideos { get; } = new();    // "001.mp4"
            public List<string> RenamedSubtitles { get; } = new(); // "001.srt"
            public List<string> Norm { get; } = new();             // "001.norm.mkv"
            public List<int> MkvParts { get; } = new();            // part indexes
            public List<int> SrtParts { get; } = new();
            public List<int> TimelineParts { get; } = new();
        }

        public sealed class AddedSet
        {
            public List<string> RenamedVideos { get; } = new();    // file mới xuất hiện
            public List<string> RenamedSubtitles { get; } = new();
        }
    }
}
