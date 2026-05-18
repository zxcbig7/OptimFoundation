using System;
using System.Diagnostics;
using System.IO;

namespace OptimFoundation.Core
{
    public static class Logging
    {
        private static readonly string _logDir  = FolderDir.Log.GetPath();
        private static string _logFile = FolderDir.Log.GetFilePath($"Log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
        private static readonly object _lock = new object();

        private static void Write(string level, string message)
        {
            string ts  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff");
            string ns  = new StackTrace().GetFrame(2)?.GetMethod()?.DeclaringType?.Namespace ?? "";
            string line = $"{ts} | {level.PadRight(5)} | [{ns}] {message}";
            lock (_lock)
            {
                Console.WriteLine(line);
                Directory.CreateDirectory(_logDir);
                File.AppendAllText(_logFile, line + Environment.NewLine);
            }
        }

        public static void Info(string message)  => Write("INFO",  message);
        public static void Debug(string message) => Write("DEBUG", message);
        public static void Warn(string message)  => Write("WARN",  message);
        public static void Error(string message) => Write("ERROR", message);

        public static void Info(string message, Stopwatch sw)
        {
            var e = sw.Elapsed;
            Info($"{message} (Elapsed {e.Hours}h {e.Minutes}m {e.Seconds}s {e.Milliseconds}ms)");
            sw.Restart();
        }

        public static void SetLogFileName(string name)
        {
            lock (_lock)
                _logFile = FolderDir.Log.GetFilePath($"{name}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
        }

        public static void ClearLogs()
        {
            lock (_lock)
                foreach (var f in Directory.GetFiles(_logDir)) File.Delete(f);
        }
    }
}
