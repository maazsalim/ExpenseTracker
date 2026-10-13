using ExpenseTracker.Models;
using Microsoft.Extensions.Options;

namespace ExpenseTracker.Services;

/// <summary>Fixes an expense's conversions to the home and secondary currencies.</summary>
public class ExpenseConverter(IExchangeRateService fx, IOptions<CurrencyOptions> options)
{
    /// <summary>Fetches the rates for the expense's currency and date, then sets the converted amounts.</summary>
    /// <exception cref="ExchangeRateUnavailableException">No rate could be found.</exception>
    public async Task ConvertAsync(Expense expense, CancellationToken ct = default)
    {
        var home = await fx.GetRateAsync(expense.Currency, options.Value.HomeCurrency, expense.Date, ct);
        var secondary = await fx.GetRateAsync(expense.Currency, options.Value.SecondaryCurrency, expense.Date, ct);

        expense.FxRate = home.Rate;
        expense.SecondaryFxRate = secondary.Rate;
        // One side is the expense's own currency (dated the expense day); the other is the published rate.
        expense.RateDate = home.RateDate < secondary.RateDate ? home.RateDate : secondary.RateDate;
        ApplyRates(expense);
    }

    /// <summary>Recalculates the converted amounts from the stored rates, e.g. when only the amount changed.</summary>
    public static void ApplyRates(Expense expense)
    {
        expense.AmountInHome = Money.Convert(expense.Amount, expense.FxRate);
        expense.AmountInSecondary = Money.Convert(expense.Amount, expense.SecondaryFxRate);
    }
}
