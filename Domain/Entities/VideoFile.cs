namespace Domain.Entities
{
    public class VideoFile
    {
        public int Index { get; }
        public string OriginalName { get; }
        public string RenamedName { get; }
        public string RenamedPath { get; }

        public VideoFile(int index, string originalName, string renamedName, string renamedPath)
        {
            Index = index;
            OriginalName = originalName;
            RenamedName = renamedName;
            RenamedPath = renamedPath;
        }
    }
}