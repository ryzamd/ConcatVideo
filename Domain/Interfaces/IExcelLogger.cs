namespace Domain.Interfaces
{
    public interface IExcelLogger : IDisposable
    {
        void LogVideo(int index, int key, string originalName, string renamedName);
        void LogSubtitle(int index, int key, string originalName, string renamedName);
        void FlushAndSave();
    }
}