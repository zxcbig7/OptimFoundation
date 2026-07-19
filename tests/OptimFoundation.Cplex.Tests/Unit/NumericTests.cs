using System;
using OptimFoundation.Core;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    public class NumericTests
    {
        [Fact]
        public void SafeRatio_NormalValues_ReturnsRatio()
        {
            double result = Numeric.SafeRatio(10, 4, context: "Test");
            Assert.Equal(2.5, result);
        }

        [Fact]
        public void SafeRatio_ZeroDenominator_ThrowsWithContext()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Numeric.SafeRatio(5, 0, context: "BigM"));
            Assert.Contains("BigM", ex.Message);
            Assert.Contains("除零", ex.Message);
        }

        [Fact]
        public void SafeRatio_NonFiniteResult_ThrowsWithContext()
        {
            // 分子分母皆為 Infinity → Infinity/Infinity = NaN
            var ex = Assert.Throws<InvalidOperationException>(
                () => Numeric.SafeRatio(double.PositiveInfinity, double.PositiveInfinity, context: "BigM"));
            Assert.Contains("BigM", ex.Message);
        }

        [Fact]
        public void SafeRatio_OverMagnitudeCeiling_ThrowsWithContext()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => Numeric.SafeRatio(1e10, 1, magnitudeCeiling: 1e9, context: "BigM"));
            Assert.Contains("BigM", ex.Message);
            Assert.Contains("過大", ex.Message);
            Assert.Contains("門檻", ex.Message);
        }

        [Fact]
        public void SafeRatio_UnderCeiling_DoesNotThrow()
        {
            double result = Numeric.SafeRatio(999_999_999, 1, magnitudeCeiling: 1e9, context: "BigM");
            Assert.Equal(999_999_999, result);
        }

        [Fact]
        public void SafeRatio_NoContext_MessageStillMeaningful()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Numeric.SafeRatio(1, 0));
            Assert.Contains("除零", ex.Message);
        }
    }
}
