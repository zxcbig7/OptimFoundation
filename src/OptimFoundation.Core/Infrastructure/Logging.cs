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
            string line = $"{ts} | {level} | {message}";
            lock (_lock)
            {
                _consoleWriter.WriteLine(line);
                FileWriter.WriteLine(line);
            }
        }

        /// <summary>一般訊息，等級標籤「資訊」；格式見 developer-guide 第 24 章。</summary>
        public static void Info(string message) => Write("資訊", message);

        /// <summary>除錯訊息，等級標籤「除錯」（與 Info 同樣會輸出）。</summary>
        public static void Debug(string message) => Write("除錯", message);

        /// <summary>警告：不中斷流程，但需要注意（如限制式被略過、規模過大）；等級標籤「警告」，必帶「結果=」。</summary>
        public static void Warn(string message) => Write("警告", message);

        /// <summary>錯誤：通常伴隨例外丟出，等級標籤「錯誤」；主動錯誤一律改用 <see cref="ErrorOnce{TException}"/>。</summary>
        public static void Error(string message) => Write("錯誤", message);

        /// <summary>
        /// 同一例外只記錄一次並原樣回傳，供外層 API 記錄後 rethrow。
        /// 寫出 <c>[事件] 說明 | 位置= 值= 原因= 細節 結果=中止</c>；說明為空時省略說明與「 | 」。
        /// </summary>
        /// <param name="exception">要記錄的例外，記錄後原樣回傳。</param>
        /// <param name="eventName">中文事件名（developer-guide 24.2），例如「實驗設定不合法」。</param>
        /// <param name="description">事件名沒說到的補充；與事件名重複時傳 null。</param>
        /// <param name="context">位置：出事的方法或步驟。</param>
        /// <param name="value">值：出問題的輸入值。</param>
        /// <param name="reason">原因：中文短語；執行時例外照原文。</param>
        /// <param name="details">其他「欄位=值」，以單一空白分隔。</param>
        public static TException ErrorOnce<TException>(
            TException exception,
            string eventName,
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

            string head = $"[{NormalizeEventName(eventName)}]";
            if (!string.IsNullOrWhiteSpace(description)) head += $" {description} |";
            string extra = string.IsNullOrWhiteSpace(details) ? string.Empty : " " + FormatField(details);
            Error($"{head} 位置={FormatField(context)} 值={FormatField(value)} 原因={FormatField(reason)}{extra} 結果=中止");
            return exception;
        }

        private static string NormalizeEventName(string eventName)
        {
            string name = (eventName ?? "框架錯誤").Trim();
            if (name.StartsWith("[", StringComparison.Ordinal)) name = name.Substring(1);
            if (name.EndsWith("]", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 1);
            return string.IsNullOrWhiteSpace(name) ? "框架錯誤" : name;
        }

        private static string FormatField(object value)
        {
            string text;
            if (value == null)
                text = "<空值>";
            else if (value is IFormattable formattable)
                text = formattable.ToString(null, CultureInfo.InvariantCulture) ?? "<空值>";
            else
                text = value.ToString() ?? "<空值>";

            return text
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        /// <summary>
        /// 在訊息後接上「耗時毫秒=」並 Restart 計時器；需累計時請自行讀 Elapsed。
        /// </summary>
        public static void Info(string message, Stopwatch sw)
        {
            string separator = message.Contains(" | ") || message.Contains("=") ? " " : " | ";
            Info($"{message}{separator}耗時毫秒={sw.ElapsedMilliseconds}");
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
