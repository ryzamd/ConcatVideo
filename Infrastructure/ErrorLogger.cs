using Domain.Interfaces;
using System.Text;

namespace Infrastructure
{
    public class ErrorLogger : IErrorLogger
    {
        private readonly string _file;
        private readonly object _lock = new object();
        public ErrorLogger(string reportDir)
        {
            _file = Path.Combine(reportDir, "errors.txt");
        }
        public void Warn(string message)
        {
            var line = $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] WARN: {message}";
            System.Console.WriteLine(line);
            lock (_lock) { File.AppendAllText(_file, line + System.Environment.NewLine, new UTF8Encoding(false)); }
        }
        public void Error(string message)
        {
            var line = $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {message}";
            System.Console.WriteLine(line);
            lock (_lock) { File.AppendAllText(_file, line + System.Environment.NewLine, new UTF8Encoding(false)); }
        }
    }
}
