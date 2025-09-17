namespace Domain.Interfaces
{
    public interface IErrorLogger
    {
        void Warn(string message);
        void Error(string message);
    }
}
