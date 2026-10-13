using ExpenseTracker.Services;

namespace ExpenseTracker.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData("100", "0.38737917", "38.74")]
    [InlineData("10.01", "0.5", "5.01")]     // 5.005 rounds half away from zero
    [InlineData("10.03", "0.5", "5.02")]     // 5.015 rounds half away from zero
    [InlineData("42.50", "1", "42.50")]
    public void Convert_RoundsToCents(string amount, string rate, string expected)
    {
        Assert.Equal(decimal.Parse(expected), Money.Convert(decimal.Parse(amount), decimal.Parse(rate)));
    }

    [Fact]
    public void Format_PrefixesCurrencyCode()
    {
        Assert.Equal("AED 1,234.50", Money.Format(1234.5m, "AED"));
    }
}
