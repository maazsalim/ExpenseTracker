using ExpenseTracker.Models;
using ExpenseTracker.Services;
using Microsoft.Extensions.Options;

namespace ExpenseTracker.Tests;

public class ExpenseConverterTests
{
    private static readonly DateOnly Saturday = new(2026, 10, 10);
    private static readonly DateOnly Friday = new(2026, 10, 9);

    private const decimal AedToCad = 0.38736555m;
    private const decimal CadToAed = 2.58154084m;

    private readonly ExpenseConverter converter = new(
        new FakeFx(),
        Options.Create(new CurrencyOptions { HomeCurrency = Currencies.CAD, SecondaryCurrency = Currencies.AED }));

    [Fact]
    public async Task AedExpense_KeepsItsAmountInAed_AndConvertsToCad()
    {
        var expense = new Expense { Amount = 900m, Currency = Currencies.AED, Date = Saturday };

        await converter.ConvertAsync(expense);

        Assert.Equal((AedToCad, 348.63m), (expense.FxRate, expense.AmountInHome));
        Assert.Equal((1m, 900m), (expense.SecondaryFxRate, expense.AmountInSecondary));
        Assert.Equal(Friday, expense.RateDate);
    }

    [Fact]
    public async Task CadExpense_KeepsItsAmountInCad_AndConvertsToAed()
    {
        var expense = new Expense { Amount = 100m, Currency = Currencies.CAD, Date = Saturday };

        await converter.ConvertAsync(expense);

        Assert.Equal((1m, 100m), (expense.FxRate, expense.AmountInHome));
        Assert.Equal((CadToAed, 258.15m), (expense.SecondaryFxRate, expense.AmountInSecondary));
        Assert.Equal(Friday, expense.RateDate);
    }

    [Fact]
    public void ApplyRates_RecalculatesFromStoredRates()
    {
        var expense = new Expense { Amount = 1000m, Currency = Currencies.AED, FxRate = AedToCad, SecondaryFxRate = 1m };

        ExpenseConverter.ApplyRates(expense);

        Assert.Equal((387.37m, 1000m), (expense.AmountInHome, expense.AmountInSecondary));
    }

    private class FakeFx : IExchangeRateService
    {
        public Task<FxQuote> GetRateAsync(string from, string to, DateOnly date, CancellationToken ct = default) =>
            Task.FromResult((from, to) switch
            {
                _ when from == to => new FxQuote(1m, date, "same-currency"),
                (Currencies.AED, Currencies.CAD) => new FxQuote(AedToCad, Friday, "test"),
                (Currencies.CAD, Currencies.AED) => new FxQuote(CadToAed, Friday, "test"),
                _ => throw new ExchangeRateUnavailableException($"{from}->{to}"),
            });
    }
}
