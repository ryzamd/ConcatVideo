namespace Domain.Entities
{
    public class VideoPart
    {
        public int PartIndex { get; }
        public string OutputPath { get; }
        public IReadOnlyList<VideoFile> Clips { get; }

        public VideoPart(int partIndex, string outputPath, IReadOnlyList<VideoFile> clips)
        {
            PartIndex = partIndex;
            OutputPath = outputPath;
            Clips = clips;
        }
    }
}