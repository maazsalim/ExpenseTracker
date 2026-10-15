using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ExpenseTracker.Services;

/// <summary>
/// Today's rate from the home currency to the secondary currency (used for budgets in the AED
/// view). Remembered per circuit (browser tab), and shared app-wide for an hour so a slow rate
/// service doesn't slow down every visit. A failed fetch isn't retried for 5 minutes.
/// </summary>
public class SecondaryCurrencyRate(
    IExchangeRateService fx,
    IOptions<CurrencyOptions> options,
    IMemoryCache cache,
    TimeProvider time,
    ILogger<SecondaryCurrencyRate> log)
{
    private static readonly TimeSpan KeepRate = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetryFailureAfter = TimeSpan.FromMinutes(5);

    private Task<FxQuote?>? quote;

    public string Home => options.Value.HomeCurrency;
    public string Currency => options.Value.SecondaryCurrency;

    /// <summary>The rate, or null if there's no secondary currency or no rate could be fetched.</summary>
    public Task<FxQuote?> GetAsync() => quote ??= LoadAsync();

    private async Task<FxQuote?> LoadAsync()
    {
        if (string.IsNullOrEmpty(Currency) || Currency == Home)
            return null;

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var key = $"todays-rate:{Home}:{Currency}:{today:yyyy-MM-dd}";
        if (cache.TryGetValue(key, out FxQuote? cached))
            return cached;

        FxQuote? result = null;
        try
        {
            result = await fx.GetRateAsync(Home, Currency, today);
        }
        catch (ExchangeRateUnavailableException ex)
        {
            log.LogWarning(ex, "No {Home}->{Currency} rate; the {Currency} view is unavailable for now", Home, Currency, Currency);
        }

        cache.Set(key, result, result is null ? RetryFailureAfter : KeepRate);
        return result;
    }
}
