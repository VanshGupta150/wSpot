using wSpot.Services;
using Xunit;

namespace wSpot.Tests
{
    public class ExpressionEvaluatorTests
    {
        [Theory]
        [InlineData("2+3*4", 14.0)]
        [InlineData("(10 - 2) * 5", 40.0)]
        [InlineData("-2^2", -4.0)]
        [InlineData("2^3^2", 512.0)]
        [InlineData("2**10", 1024.0)]
        [InlineData("sqrt(144) + 5", 17.0)]
        [InlineData("sin(pi/2)", 1.0)]
        [InlineData("80 * 0.15", 12.0)]
        [InlineData("abs(-25) + 5", 30.0)]
        public void TryEvaluate_ValidExpressions_ReturnsExpectedValue(string input, double expected)
        {
            bool success = ExpressionEvaluator.TryEvaluate(input, out double actual);
            Assert.True(success);
            Assert.Equal(expected, actual, precision: 5);
        }

        [Theory]
        [InlineData("5")]
        [InlineData("42")]
        [InlineData("pi")]
        [InlineData("e")]
        [InlineData("tau")]
        [InlineData("vs2022")]
        [InlineData("7zip")]
        [InlineData("")]
        [InlineData("   ")]
        public void TryEvaluate_SingleNumberOrLiteral_ReturnsFalse(string input)
        {
            bool success = ExpressionEvaluator.TryEvaluate(input, out _);
            Assert.False(success);
        }

        [Theory]
        [InlineData("1/0")]
        [InlineData("sqrt(-1)")]
        [InlineData("log(-5)")]
        [InlineData("2++")]
        [InlineData("(5+2")]
        public void TryEvaluate_MathErrorsAndMalformed_ReturnsFalse(string input)
        {
            bool success = ExpressionEvaluator.TryEvaluate(input, out _);
            Assert.False(success);
        }
    }
}
