using Enums;

namespace Models
{
    public class RuntimeOptions
    {
        public OnMissingSubtitleMode OnMissingSubtitle { get; set; } = OnMissingSubtitleMode.Skip;
        public bool StrictMapping { get; set; } = false;
        public string? TargetFormatForced { get; set; } = null;
        public bool ReencodeVideoWithGpu { get; set; } = false;
    }
}