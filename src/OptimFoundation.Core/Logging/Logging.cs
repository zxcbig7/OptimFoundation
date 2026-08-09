using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 框架統一的 log 出口：每筆同時寫 Console 與 log 檔，格式為「時間 | 等級 | 訊息」。
    /// 檔案延遲建立（首次寫入才開檔），寫入以 lock 保護，可多執行緒呼叫。
    /// </summary>
    public static class Logging
    {
        private const string ErrorLoggedDataKey = "OptimFoundation.ErrorLogged";
        private static readonly string _logDir = FolderDir.Log.GetPath();
        private static string _logFile = FolderDir.Log.GetFilePath($"Log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
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

        /// <summary>延遲開檔：首次寫入才建 log 檔，避免 SetLogFileName 換檔前留下空的孤兒 Log_*.txt。呼叫端須持有 _lock。</summary>
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
        /// 記錄框架即將中止的例外。同一個例外物件只會記錄一次，外層公開 API
        /// 可安全地再次呼叫後用 <c>throw;</c> 原樣拋出。
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
        /// 印訊息並附上 Stopwatch 的經過時間。
        /// ⚠ 有副作用：印完會 <b>Restart</b> 這個 Stopwatch，讓下一段從零開始計時（連續分段計時的慣用寫法）。
        /// 要保留累計時間 NEVER 用這個 overload。
        /// </summary>
        public static void Info(string message, Stopwatch sw)
        {
            var e = sw.Elapsed;
            Info($"{message} (Elapsed {e.Hours}h {e.Minutes}m {e.Seconds}s {e.Milliseconds}ms)");
            sw.Restart();
        }

        /// <summary>
        /// 改用新的 log 檔名（實際檔名為 {name}_{時間戳}.txt，非法字元會被換成 '-'）。
        /// 會關掉目前的 log 檔並在下次寫入時開新檔；已寫入舊檔的內容留在原檔。
        /// OptProject 執行時會自動以專案名呼叫，一般不需自己叫。
        /// </summary>
        public static void SetLogFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '-');
            lock (_lock)
            {
                _logFile = FolderDir.Log.GetFilePath($"{name}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
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
        /// ⚠ 破壞性：刪掉 Logs 資料夾內的<b>所有</b>檔案（含本次執行正在寫的），不可回復。
        /// 例行清理 ALWAYS 改用 OptProject 的 retentionDays 保留期機制，只清超過天數的舊檔。
        /// </summary>
        public static void ClearLogs()
        {
            lock (_lock)
            {
                if (!Directory.Exists(_logDir)) return;
                _fileWriter?.Dispose();   // 放掉目前 log 檔的 handle，否則刪到自己會 IOException
                _fileWriter = null;
                foreach (var f in Directory.GetFiles(_logDir)) File.Delete(f);
            }
        }
    }
}
