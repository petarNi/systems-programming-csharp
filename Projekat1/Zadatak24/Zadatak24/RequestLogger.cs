using System;
using System.IO;
using System.Text;

namespace Zadatak24
{
    public static class RequestLogger
    {
        private static readonly object _lock = new object();
        private static string _logDir;
        private static string _logFile;

        public static void Init(string logDir)
        {
            _logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logDir);
            _logFile = Path.Combine(_logDir, "server.log");
            Directory.CreateDirectory(_logDir);
        }

        public static void Log(string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            lock (_lock)
            {
                Directory.CreateDirectory(_logDir);
                File.AppendAllText(_logFile, line + Environment.NewLine, Encoding.UTF8);
            }
            Console.WriteLine(line);
        }
    }
}
