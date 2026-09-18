#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 名稱標準化工具：將 Set row / Parameter row / 其他維度值格式化為 solver-safe token，並組成完整模型名稱。
    /// </summary>
    internal static class ModelNaming
    {
        internal const char Separator = '@';
        internal const string DateFormat = "yyyy_MM_dd";
        internal const string DateTimeFormat = "yyyy_MM_dd_HH_mm_ss";

        /// <summary>解析日期 token 時可接受的格式：帶時分秒與純日期兩種。</summary>
        internal static readonly string[] DateFormats = [DateTimeFormat, DateFormat];

        private static readonly char[] InvalidTokenCharacters =
        [
            '+', '-', '*', '/', '^', '<', '>', '=', ':', ',', '\\', Separator
        ];

        /// <summary>將單一維度值格式化為 solver-safe token。</summary>
        internal static string Token(string context, object? value)
        {
            if (value == null)
                ThrowInvalid(context, null, "value_is_null");

            string token;
            if (value is DateTime date)
            {
                token = FormatDate(context, date);
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

            ValidateToken(context, token); // 轉換後驗證
            return token;
        }

        /// <summary>日期 token：粒度到秒，純日期維持 <see cref="DateFormat"/>，帶時分秒才展開成 <see cref="DateTimeFormat"/>。</summary>
        internal static string FormatDate(string context, DateTime value)
        {
            // 秒以下靜默截掉會讓兩個不同時刻產生同一個 token，key 悄悄相撞
            if (value.Ticks % TimeSpan.TicksPerSecond != 0)
            {
                ThrowInvalid(context, value.ToString("O", CultureInfo.InvariantCulture), "datetime_subsecond_precision");
            }

            return value.ToString(
                value.TimeOfDay == TimeSpan.Zero ? DateFormat : DateTimeFormat,
                CultureInfo.InvariantCulture);
        }

        /// <summary>以 head 與維度值組成完整模型名稱；多維 Set row 會展開成多個 token。</summary>
        internal static string Compose(string head, params object?[] dims)
        {
            ValidateToken("model name head", head);
            if (char.IsDigit(head[0]) || head[0] == '.')
                ThrowInvalid("model name head", head, "invalid_leading_character");
            if (dims == null)
                ThrowInvalid(head, null, "dimensions_array_is_null");

            var tokens = new List<string>(dims.Length);
            for (int index = 0; index < dims.Length; index++)
            {
                object? value = dims[index];
                if (value is SetRowBase row)
                {
                    string rowText = row.ToString();
                    if (string.IsNullOrEmpty(rowText))
                        ThrowInvalid($"{head} dim #{index + 1}", rowText, "set_row_has_no_dimensions");

                    string[] rowTokens = rowText.Split(Separator);
                    for (int rowIndex = 0; rowIndex < rowTokens.Length; rowIndex++)
                    {
                        ValidateToken($"{head} dim #{index + 1}.{rowIndex + 1}", rowTokens[rowIndex]);
                        tokens.Add(rowTokens[rowIndex]);
                    }
                }
                else
                {
                    tokens.Add(Token($"{head} dim #{index + 1}", value));
                }
            }

            return tokens.Count == 0
                ? head
                : head + Separator + string.Join(Separator, tokens);
        }

        /// <summary>驗證既有 string overload 收到的完整名稱；合法時原樣回傳。</summary>
        internal static string ValidateComposedName(string context, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                ThrowInvalid(context, name, "name_is_empty");

            string[] tokens = name.Split(Separator);
            ValidateToken($"{context} head", tokens[0]);
            if (char.IsDigit(tokens[0][0]) || tokens[0][0] == '.')
                ThrowInvalid($"{context} head", tokens[0], "invalid_leading_character");

            for (int index = 1; index < tokens.Length; index++)
                ValidateToken($"{context} token #{index}", tokens[index]);

            return name;
        }

        /// <summary>驗證已格式化 token；違規時先記 Error Log 再拋例外。</summary>
        internal static void ValidateToken(string context, string token)
        {
            if (string.IsNullOrEmpty(token))
                ThrowInvalid(context, token, "token_is_empty");

            if (token.IndexOfAny(InvalidTokenCharacters) >= 0)
                ThrowInvalid(context, token, "contains_reserved_character");

            for (int index = 0; index < token.Length; index++)
                if (char.IsWhiteSpace(token[index]))
                    ThrowInvalid(context, token, "contains_whitespace");
        }

        [DoesNotReturn]
        private static void ThrowInvalid(string context, string? value, string reason)
        {
            string safeValue = (value ?? "<null>")
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
            var exception = new ArgumentException(
                $"模型名稱不合法：context={context}, value='{safeValue}', reason={reason}。");
            throw Logging.ErrorOnce(
                exception,
                "MODEL_NAME_INVALID",
                "模型名稱驗證失敗",
                context,
                safeValue,
                reason);
        }
    }
}
