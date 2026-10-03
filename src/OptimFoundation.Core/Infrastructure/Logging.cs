using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 將「時間 | 等級 | 訊息」寫入 Console 與 log；檔案首次寫入才建立，支援多執行緒。
    /// </summary>
    public static class Logging
    {
        private const string ErrorLoggedDataKey = "OptimFoundation.ErrorLogged";
        private static readonly string _logDir = FolderDir.Log.GetPath();
        private static string _logFile = FolderDir.Log.GetPathFile($"Log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
        private static string _logFileName;
        private static readonly object _lock = new object();
        private static readonly Encoding _utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly Encoding _utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        private static readonly StreamWriter _consoleWriter;
        private static StreamWriter _fileWriter;

        static Logging()
        {
            Console.OutputEncoding = _utf8;
            _consoleWriter = new StreamWriter(Console.OpenStandardOutput(), _utf8) { AutoFlush = true };
            Console.SetOut(_consoleWriter);
        }

        /// <summary>首次寫入才開啟 log 檔，避免 SetLogFileName 指定檔名前先產生空檔；存取時須先取得 _lock。</summary>
        private static StreamWriter FileWriter
        {
            get
            {
                if (_fileWriter == null)
                {
                    Directory.CreateDirectory(_logDir);
                    _fileWriter = new StreamWriter(_logFile, append: true, _utf8Bom) { AutoFlush = true };
                }
                return _fileWriter;
            }
        }

        private static void Write(string level, string message)
        {
            string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string line = $"{ts} | {level.PadRight(5)} | {message}";
            lock (_lock)
            {
                _consoleWriter.WriteLine(line);
                FileWriter.WriteLine(line);
            }
        }

        /// <summary>一般訊息。</summary>
        public static void Info(string message) => Write("INFO", message);

        /// <summary>除錯訊息（與 Info 同樣會輸出，只是等級標籤不同）。</summary>
        public static void Debug(string message) => Write("DEBUG", message);

        /// <summary>警告：不中斷流程，但需要注意（如限制式被略過、規模超標）。</summary>
        public static void Warn(string message) => Write("WARN", message);

        /// <summary>錯誤：通常伴隨例外拋出，訊息格式為 [ERROR_CODE] 說明 | key=value。</summary>
        public static void Error(string message) => Write("ERROR", message);

        /// <summary>
        /// 同一例外只記錄一次並原樣回傳，供外層 API 記錄後 rethrow。
        /// </summary>
        public static TException ErrorOnce<TException>(
            TException exception,
            string eventCode,
            string description,
            string context,
            object value,
            string reason,
            string details = null)
            where TException : Exception
        {
            lock (exception.Data)
            {
                if (exception.Data.Contains(ErrorLoggedDataKey))
                    return exception;

                exception.Data[ErrorLoggedDataKey] = true;
            }

            string code = NormalizeEventCode(eventCode);
            string extra = string.IsNullOrWhiteSpace(details) ? string.Empty : " " + FormatField(details);
            Error($"[{code}] {description} | context={FormatField(context)} value={FormatField(value)} reason={FormatField(reason)}{extra} result=aborted");
            return exception;
        }

        private static string NormalizeEventCode(string eventCode)
        {
            string code = (eventCode ?? "FRAMEWORK_ERROR").Trim();
            if (code.StartsWith("[", StringComparison.Ordinal)) code = code.Substring(1);
            if (code.EndsWith("]", StringComparison.Ordinal)) code = code.Substring(0, code.Length - 1);
            return string.IsNullOrWhiteSpace(code) ? "FRAMEWORK_ERROR" : code;
        }

        private static string FormatField(object value)
        {
            string text;
            if (value == null)
                text = "<null>";
            else if (value is IFormattable formattable)
                text = formattable.ToString(null, CultureInfo.InvariantCulture) ?? "<null>";
            else
                text = value.ToString() ?? "<null>";

            return text
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        /// <summary>
        /// 記錄訊息與經過時間後 Restart 計時器；需累計時請自行讀 Elapsed。
        /// </summary>
        public static void Info(string message, Stopwatch sw)
        {
            var e = sw.Elapsed;
            Info($"{message} (Elapsed {e.Hours}h {e.Minutes}m {e.Seconds}s {e.Milliseconds}ms)");
            sw.Restart();
        }

        /// <summary>
        /// 改用 {name}_{時間戳}.txt，非法字元替換為 -；同名沿用原檔，異名關閉舊檔並於下次寫入開新檔。
        /// </summary>
        public static void SetLogFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '-');
            lock (_lock)
            {
                if (string.Equals(_logFileName, name, StringComparison.Ordinal)) return;

                _logFileName = name;
                _logFile = FolderDir.Log.GetPathFile($"{name}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
                _fileWriter?.Dispose();
                _fileWriter = null;
            }
        }

        /// <summary>只寫入 log 檔，不輸出到 Console。用於避免 CPLEX 即時輸出後再次印出。</summary>
        public static void WriteToFile(string message)
        {
            lock (_lock)
                FileWriter.WriteLine(message);
        }

        /// <summary>
        /// 刪除 FolderDir.Log 內的<b>所有</b>檔案，包含本次執行的 log，刪除後無法由此方法回復。
        /// 例行清理請用 OptProject 的 retentionDays，只刪除超過保留天數的舊檔。
        /// </summary>
        public static void ClearLogs()
        {
            lock (_lock)
            {
                if (!Directory.Exists(_logDir)) return;
                _fileWriter?.Dispose();   // 先關閉目前的 log 檔，避免刪除使用中的檔案時發生 IOException。
                _fileWriter = null;
                foreach (var f in Directory.GetFiles(_logDir)) File.Delete(f);
            }
        }
    }
}
