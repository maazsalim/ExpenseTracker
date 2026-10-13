using ExpenseTracker.Models;
using Microsoft.Extensions.Options;

namespace ExpenseTracker.Services;

/// <summary>
/// Which currency totals are shown in (home CAD or secondary AED), shared by every page in
/// a circuit (browser tab). Pages listen to <see cref="Changed"/> and re-render.
/// </summary>
public class DisplayCurrency(SecondaryCurrencyRate todaysRate, IOptions<CurrencyOptions> options)
{
    private FxQuote? rate;

    public string Home => options.Value.HomeCurrency;
    public string Secondary => options.Value.SecondaryCurrency;

    /// <summary>The currency totals are currently shown in.</summary>
    public string Current { get; private set; } = options.Value.HomeCurrency;

    public bool ShowingSecondary => Current == Secondary;

    /// <summary>
    /// Whether the secondary currency can be shown. Budgets are set in the home currency and
    /// need today's rate to be shown in the secondary one.
    /// </summary>
    public bool SecondaryAvailable => rate is not null;

    public event Action? Changed;

    /// <summary>Fetches today's rate (once per circuit) so <see cref="SecondaryAvailable"/> is known.</summary>
    public async Task InitializeAsync() => rate = await todaysRate.GetAsync();

    /// <summary>Switches the display currency. Returns false if it can't be shown.</summary>
    public async Task<bool> SetAsync(string currency)
    {
        if (currency == Current)
            return true;
        if (currency != Home && currency != Secondary)
            return false;
        if (currency == Secondary)
        {
            await InitializeAsync();
            if (rate is null)
                return false;
        }

        Current = currency;
        Changed?.Invoke();
        return true;
    }

    /// <summary>The expense's amount in the display currency, as fixed when it was saved.</summary>
    public decimal Of(Expense e) => ShowingSecondary ? e.AmountInSecondary : e.AmountInHome;

    /// <summary>The rate used for <see cref="Of"/>.</summary>
    public decimal RateOf(Expense e) => ShowingSecondary ? e.SecondaryFxRate : e.FxRate;

    /// <summary>A budget (set in the home currency) in the display currency, at today's rate.</summary>
    public decimal Budget(decimal homeAmount) =>
        ShowingSecondary && rate is not null ? Money.Convert(homeAmount, rate.Rate) : homeAmount;

    /// <summary>Today's home→secondary rate, if known.</summary>
    public decimal? TodaysRate => rate?.Rate;

    public string Format(decimal amount) => Money.Format(amount, Current);
}
