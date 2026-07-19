using System;

namespace OptimFoundation.Core
{
    /// <summary>數值 sanity helper：供 BigM 等推導值選用防呆比值（見框架資料防護規格）。</summary>
    public static class Numeric
    {
        /// <summary>
        /// 防呆比值：分母為 0 / 結果非有限 / 超過量級門檻一律 throw（訊息含 context）；否則回傳比值。
        /// magnitudeCeiling 預設 1e9 是「衍生值（如 BigM）」的嚴格門檻，與 DataValidator.MaxMagnitude（原始資料值，1e15）刻意不同，勿統一。
        /// </summary>
        public static double SafeRatio(double numerator, double denominator, double magnitudeCeiling = 1e9, string context = null)
        {
            string ctx = context ?? "(未提供 context)";

            if (denominator == 0)
                throw new InvalidOperationException($"[Numeric.SafeRatio] {ctx} 除零：分母為 0（分子 = {numerator}）。");

            double result = numerator / denominator;

            if (double.IsNaN(result) || double.IsInfinity(result))
                throw new InvalidOperationException(
                    $"[Numeric.SafeRatio] {ctx} 計算結果非有限值（{result}）：分子 = {numerator}，分母 = {denominator}。");

            if (Math.Abs(result) > magnitudeCeiling)
                throw new InvalidOperationException(
                    $"[Numeric.SafeRatio] {ctx} 比值過大（實際值 {result}，門檻 {magnitudeCeiling}）會使 solver 數值不穩" +
                    "（如 BigM 過大導致 relaxation 鬆弛、branch 爆炸）。");

            return result;
        }
    }
}
