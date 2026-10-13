using ExpenseTracker.Models;
using ExpenseTracker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExpenseTracker.Tests;

public class DisplayCurrencyTests
{
    private readonly FakeFx fx = new();

    private DisplayCurrency Create()
    {
        var options = Options.Create(new CurrencyOptions { HomeCurrency = Currencies.CAD, SecondaryCurrency = Currencies.AED });
        var rate = new SecondaryCurrencyRate(fx, options, TimeProvider.System, NullLogger<SecondaryCurrencyRate>.Instance);
        return new DisplayCurrency(rate, options);
    }

    private static readonly Expense AedExpense = new()
    {
        Amount = 900m, Currency = Currencies.AED,
        FxRate = 0.38834m, AmountInHome = 349.51m,
        SecondaryFxRate = 1m, AmountInSecondary = 900m,
    };

    [Fact]
    public void StartsInTheHomeCurrency()
    {
        var display = Create();

        Assert.Equal(Currencies.CAD, display.Current);
        Assert.Equal(349.51m, display.Of(AedExpense));
        Assert.Equal(500m, display.Budget(500m));
    }

    [Fact]
    public async Task Switching_UsesTheStoredSecondaryAmount_AndConvertsBudgetsAtTodaysRate()
    {
        var display = Create();
        var changed = 0;
        display.Changed += () => changed++;

        Assert.True(await display.SetAsync(Currencies.AED));

        Assert.Equal(1, changed);
        Assert.Equal(900m, display.Of(AedExpense));
        Assert.Equal(1m, display.RateOf(AedExpense));
        Assert.Equal(1290.75m, display.Budget(500m)); // 500 × 2.5815
        Assert.Equal("AED 900.00", display.Format(display.Of(AedExpense)));
    }

    [Fact]
    public async Task WithoutTodaysRate_StaysInTheHomeCurrency()
    {
        fx.Fail = true;
        var display = Create();

        Assert.False(await display.SetAsync(Currencies.AED));
        Assert.Equal(Currencies.CAD, display.Current);
        Assert.False(display.SecondaryAvailable);
    }

    [Fact]
    public async Task UnknownCurrency_IsRejected()
    {
        Assert.False(await Create().SetAsync("EUR"));
    }

    private class FakeFx : IExchangeRateService
    {
        public bool Fail { get; set; }

        public Task<FxQuote> GetRateAsync(string from, string to, DateOnly date, CancellationToken ct = default) =>
            Fail
                ? throw new ExchangeRateUnavailableException("offline")
                : Task.FromResult(new FxQuote(2.5815m, date, "test"));
    }
}
