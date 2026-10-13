using Microsoft.Extensions.Options;

namespace ExpenseTracker.Services;

/// <summary>
/// Today's rate from the home currency to the secondary display currency, fetched once per
/// circuit (browser tab) and shared by every total on the page.
/// </summary>
public class SecondaryCurrencyRate(
    IExchangeRateService fx,
    IOptions<CurrencyOptions> options,
    TimeProvider time,
    ILogger<SecondaryCurrencyRate> log)
{
    private Task<FxQuote?>? quote;

    public string Home => options.Value.HomeCurrency;
    public string Currency => options.Value.SecondaryCurrency;

    /// <summary>The rate, or null if there's no secondary currency or no rate could be fetched.</summary>
    public Task<FxQuote?> GetAsync() => quote ??= LoadAsync();

    private async Task<FxQuote?> LoadAsync()
    {
        if (string.IsNullOrEmpty(Currency) || Currency == Home)
            return null;

        try
        {
            var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
            return await fx.GetRateAsync(Home, Currency, today);
        }
        catch (ExchangeRateUnavailableException ex)
        {
            log.LogWarning(ex, "No {Home}->{Currency} rate; hiding secondary amounts", Home, Currency);
            return null;
        }
    }
}
