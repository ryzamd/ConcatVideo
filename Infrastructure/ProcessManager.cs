using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure
{
    /// <summary>
    /// Track mọi Process con (ffmpeg/ffprobe). Đảm bảo kill khi Ctrl+C / ProcessExit.
    /// </summary>
    public static class ProcessManager
    {
        private static readonly ConcurrentDictionary<int, Process> _procs = new();

        static ProcessManager()
        {
            AppDomain.CurrentDomain.ProcessExit += (_, __) => KillAll("ProcessExit");
            AppDomain.CurrentDomain.UnhandledException += (_, __) => KillAll("UnhandledException");
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true; // chủ động kết thúc
                KillAll("CancelKeyPress");
                Environment.Exit(130);
            };
        }

        public static Process Start(ProcessStartInfo psi)
        {
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

            p.Exited += (_, __) => _procs.TryRemove(p.Id, out Process? _);

            if (!p.Start()) throw new InvalidOperationException($"Cannot start process: {psi.FileName}");
                _procs[p.Id] = p;
            return p;
        }

        public static void KillAll(string reason)
        {
            foreach (var kv in _procs)
            {
                try { if (!kv.Value.HasExited) kv.Value.Kill(true); } catch { }
            }
            _procs.Clear();
        }
    }
}
