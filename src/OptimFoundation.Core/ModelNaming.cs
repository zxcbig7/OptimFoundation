#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 驗證維度值並以 @ 組成求解器名稱；多維 Set 會展開。
    /// </summary>
    internal static class ModelNaming
    {
        internal const char Separator = '@';
        internal const string DateFormat = "yyyy_MM_dd";
        internal const string DateTimeFormat = "yyyy_MM_dd_HH_mm_ss";

        /// <summary>解析日期 token 時可接受的格式：帶時分秒與純日期兩種。</summary>
        internal static readonly string[] DateFormats = [DateTimeFormat, DateFormat];

        /// <summary>名稱上限（UTF-8 位元組）：CPLEX 讀 .lp 時超過就截斷，截斷後不同名稱可能撞名。</summary>
        internal const int MaxNameBytes = 254;

        // 匯出 .lp 時 CPLEX 不接受的字元：只要有一個名稱含這些字元，整份 .lp 的名稱都會被改掉。@ 是維度分隔符。
        private static readonly char[] InvalidTokenCharacters =
        [
            '+', '-', '*', '/', '^', '<', '>', '=', ':', '\\', '[', ']', '|', Separator
        ];

        /// <summary>把一個維度值轉成名稱片段，並檢查是否含空白、控制字元或保留字元；不合法時拋出例外。</summary>
        internal static string Token(string context, object? value)
        {
            if (!TryToken(value, out string? token, out string? reason))
                ThrowInvalid(context, token, reason);
            return token;
        }

        /// <summary>
        /// 與 <see cref="Token"/> 同一套規則，但不記 Log、不拋例外：不合法時回 false，
        /// <paramref name="token"/> 為違規值的顯示字串（null 值為 null），<paramref name="reason"/> 為原因代碼。
        /// </summary>
        internal static bool TryToken(
            object? value,
            [NotNullWhen(true)] out string? token,
            [NotNullWhen(false)] out string? reason)
        {
            if (value == null)
            {
                token = null;
                reason = "值為空";
                return false;
            }

            if (value is DateTime date)
            {
                if (!TryFormatDate(date, out string formatted))
                {
                    token = formatted;
                    reason = "日期含秒以下精度";
                    return false;
                }
                token = formatted;
            }
            else if (value is string text)
            {
                token = text;
            }
            else if (value is IFormattable formattable)
            {
                token = formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;
            }
            else
            {
                token = value.ToString() ?? string.Empty;
            }

            reason = InvalidTokenReason(token); // 轉換後驗證
            if (reason != null)
                return false;
            return true;
        }

        /// <summary>把日期轉成名稱片段：純日期使用 <see cref="DateFormat"/>，有時間則使用 <see cref="DateTimeFormat"/>；不接受秒以下精度。</summary>
        internal static string FormatDate(string context, DateTime value)
        {
            if (!TryFormatDate(value, out string token))
                ThrowInvalid(context, token, "日期含秒以下精度");
            return token;
        }

        /// <summary>依 <see cref="DateFormats"/> 解析名稱片段格式的日期，讓框架寫出的日期（例：解檔的 2026_06_01）讀得回來。</summary>
        internal static bool TryParseDate(string text, out DateTime date)
            => DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

        // 不接受的日期會以保留完整精度的 "O" 格式回傳，供錯誤訊息顯示。
        private static bool TryFormatDate(DateTime value, out string token)
        {
            // 捨去秒以下精度會讓不同時刻撞名。
            if (value.Ticks % TimeSpan.TicksPerSecond != 0)
            {
                token = value.ToString("O", CultureInfo.InvariantCulture);
                return false;
            }

            token = value.ToString(
                value.TimeOfDay == TimeSpan.Zero ? DateFormat : DateTimeFormat,
                CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>以 head 作為名稱開頭，再用 @ 接上維度值；一筆多維 Set 資料會展開成多個名稱片段。</summary>
        internal static string Compose(string head, params object?[] dims)
        {
            ValidateToken("模型名稱開頭", head);
            if (IsInvalidLeadingCharacter(head[0]))
                ThrowInvalid("模型名稱開頭", head, "開頭字元不合法");
            if (dims == null)
                ThrowInvalid(head, null, "維度陣列為空");

            var tokens = new List<string>(dims.Length);
            for (int index = 0; index < dims.Length; index++)
            {
                object? value = dims[index];
                if (value is SetRowBase row)
                {
                    // 直接取維度值，不經 ToString（Set 的 ToString 含類別名）。
                    string[] rowTokens = row.KeyParts();
                    if (rowTokens.Length == 0)
                        ThrowInvalid($"{head} 維度 #{index + 1}", row.GetType().Name, "集合資料列沒有任何維度");
                    tokens.AddRange(rowTokens);
                }
                else
                {
                    tokens.Add(Token($"{head} 維度 #{index + 1}", value));
                }
            }

            string name = tokens.Count == 0
                ? head
                : head + Separator + string.Join(Separator, tokens);
            ValidateLength(head, name);
            return name;
        }

        /// <summary> 檢查呼叫端直接傳入的完整名稱，包括開頭與 @ 分隔的每一段；合法時原樣回傳。</summary>
        internal static string ValidateComposedName(string context, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                ThrowInvalid(context, name, "名稱為空");

            string[] tokens = name.Split(Separator);
            ValidateToken($"{context} 開頭", tokens[0]);
            if (IsInvalidLeadingCharacter(tokens[0][0]))
                ThrowInvalid($"{context} 開頭", tokens[0], "開頭字元不合法");

            for (int index = 1; index < tokens.Length; index++)
                ValidateToken($"{context} 片段 #{index}", tokens[index]);

            ValidateLength(context, name);
            return name;
        }

        // .lp 會把數字、句點、e/E 開頭的名稱改名（e/E 保留給科學記號）。
        // 非 ASCII 首字元的 UTF-16 低位元組落在控制字元區（例：上 U+4E0A、三 U+4E09）時，
        // CPLEX 建模直接報 Error 1236 Control character，只有開頭會這樣，放在中間沒事。
        private static bool IsInvalidLeadingCharacter(char c)
            => char.IsDigit(c) || c == '.' || c == 'e' || c == 'E'
                || (c > 0x7F && (c & 0xFF) is (>= 0x01 and <= 0x1F) or 0x7F);

        private static void ValidateLength(string context, string name)
        {
            if (Encoding.UTF8.GetByteCount(name) > MaxNameBytes)
                ThrowInvalid(context, name, $"超過{MaxNameBytes}位元組");
        }

        /// <summary>驗證已格式化 token；違規時先記 Error Log 再拋例外。</summary>
        internal static void ValidateToken(string context, string token)
        {
            string? reason = InvalidTokenReason(token);
            if (reason != null)
                ThrowInvalid(context, token, reason);
        }

        private static string? InvalidTokenReason(string token)
        {
            if (string.IsNullOrEmpty(token))
                return "名稱片段為空";

            if (token.IndexOfAny(InvalidTokenCharacters) >= 0)
                return "含保留字元";

            for (int index = 0; index < token.Length; index++)
            {
                if (char.IsWhiteSpace(token[index]))
                    return "含空白字元";
                if (char.IsControl(token[index]))
                    return "含控制字元";
            }

            return null;
        }

        /// <summary>Log 用的單行顯示值：null 顯示為 &lt;空值&gt;，換行字元跳脫。</summary>
        internal static string DisplayValue(string? value)
            => (value ?? "<空值>")
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);

        [DoesNotReturn]
        private static void ThrowInvalid(string context, string? value, string reason)
        {
            string safeValue = DisplayValue(value);
            var exception = new ArgumentException(
                $"模型名稱不合法：位置={context}，值='{safeValue}'，原因={reason}");
            throw Logging.ErrorOnce(
                exception,
                "模型名稱不合法",
                null,
                context,
                safeValue,
                reason);
        }
    }
}
