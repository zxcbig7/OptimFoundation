using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace OptimFoundation.Core
{
    public static class Logging
    {
        private static readonly string _logDir = FolderDir.Log.GetPath();
        private static string _logFile = FolderDir.Log.GetFilePath($"Log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
        private static readonly object _lock = new object();
        private static readonly Encoding _utf8    = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly Encoding _utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        private static readonly StreamWriter _consoleWriter;
        private static StreamWriter _fileWriter;

        static Logging()
        {
            Console.OutputEncoding = _utf8;
            _consoleWriter = new StreamWriter(Console.OpenStandardOutput(), _utf8) { AutoFlush = true };
            Console.SetOut(_consoleWriter);
            _fileWriter = CreateFileWriter();
        }

        private static StreamWriter CreateFileWriter()
        {
            Directory.CreateDirectory(_logDir);
            return new StreamWriter(_logFile, append: true, _utf8Bom) { AutoFlush = true };
        }

        private static void Write(string level, string message)
        {
            string ts  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff");
            string ns  = new StackTrace().GetFrame(2)?.GetMethod()?.DeclaringType?.Namespace ?? "";
            string line = $"{ts} | {level.PadRight(5)} | [{ns}] {message}";
            lock (_lock)
            {
                _consoleWriter.WriteLine(line);
                _fileWriter.WriteLine(line);
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
            {
                _logFile = FolderDir.Log.GetFilePath($"{name}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
                _fileWriter?.Dispose();
                _fileWriter = CreateFileWriter();
            }
        }

        /// <summary>只寫入 log 檔，不輸出到 Console。用於避免 CPLEX 即時輸出後再次印出。</summary>
        public static void WriteToFile(string message)
        {
            lock (_lock)
                _fileWriter.WriteLine(message);
        }

        public static void ClearLogs()
        {
            lock (_lock)
                foreach (var f in Directory.GetFiles(_logDir)) File.Delete(f);
        }
    }
}
