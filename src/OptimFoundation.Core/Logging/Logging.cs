using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace OptimFoundation.Core
{
    public static class Logging
    {
        private static readonly string _logDir  = FolderDir.Log.GetPath();
        private static string _logFile = FolderDir.Log.GetFilePath($"Log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
        private static readonly object _lock = new object();
        private static readonly Encoding _utf8    = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly Encoding _utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        private static readonly StreamWriter _consoleWriter;

        static Logging()
        {
            // 直接接管 Console.Out，鎖定 UTF-8，防止 CPLEX 或其他 native 呼叫覆蓋
            Console.OutputEncoding = _utf8;
            _consoleWriter = new StreamWriter(Console.OpenStandardOutput(), _utf8) { AutoFlush = true };
            Console.SetOut(_consoleWriter);
        }

        private static void Write(string level, string message)
        {
            string ts  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff");
            string ns  = new StackTrace().GetFrame(2)?.GetMethod()?.DeclaringType?.Namespace ?? "";
            string line = $"{ts} | {level.PadRight(5)} | [{ns}] {message}";
            lock (_lock)
            {
                _consoleWriter.WriteLine(line);
                Directory.CreateDirectory(_logDir);
                // append: true → 新檔時 StreamWriter 寫 BOM 一次，後續 append 不重複
                using var sw = new StreamWriter(_logFile, append: true, encoding: _utf8Bom);
                sw.WriteLine(line);
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

        /// <summary>只寫入 log 檔，不輸出到 Console。用於避免 CPLEX 即時輸出後再次印出。</summary>
        public static void WriteToFile(string message)
        {
            lock (_lock)
            {
                Directory.CreateDirectory(_logDir);
                using var sw = new StreamWriter(_logFile, append: true, encoding: _utf8Bom);
                sw.WriteLine(message);
            }
        }

        public static void ClearLogs()
        {
            lock (_lock)
                foreach (var f in Directory.GetFiles(_logDir)) File.Delete(f);
        }
    }
}
