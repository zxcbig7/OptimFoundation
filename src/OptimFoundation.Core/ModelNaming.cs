#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 模型元素名稱的唯一序列化與驗證入口。資料層日期格式不屬於此類別。
    /// </summary>
    internal static class ModelNaming
    {
        internal const char Separator = '@';
        internal const string DateFormat = "yyyy_MM_dd";

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
                if (date.TimeOfDay != TimeSpan.Zero)
                    ThrowInvalid(context, date.ToString("O", CultureInfo.InvariantCulture), "datetime_contains_time");

                token = date.ToString(DateFormat, CultureInfo.InvariantCulture);
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

            ValidateToken(context, token);
            return token;
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
