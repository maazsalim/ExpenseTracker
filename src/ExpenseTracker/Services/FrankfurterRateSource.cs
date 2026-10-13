using System.Net.Http.Json;

namespace ExpenseTracker.Services;

/// <summary>
/// ECB reference rates from https://frankfurter.dev (free, no key). ECB doesn't publish AED,
/// and publishes nothing on weekends/holidays; Frankfurter then returns the previous business day.
/// </summary>
public class FrankfurterRateSource(HttpClient http) : IUsdRateSource
{
    public string Name => "frankfurter";

    private record Response(DateOnly Date, Dictionary<string, decimal> Rates);

    public async Task<UsdRate> GetAsync(string currency, DateOnly date, CancellationToken ct = default)
    {
        var url = $"v1/{date:yyyy-MM-dd}?base=USD&symbols={Uri.EscapeDataString(currency)}";
        var response = await http.GetFromJsonAsync<Response>(url, ct)
            ?? throw new ExchangeRateUnavailableException($"Empty response from Frankfurter for {currency}.");

        if (!response.Rates.TryGetValue(currency, out var rate))
            throw new ExchangeRateUnavailableException($"Frankfurter has no {currency} rate for {date}.");

        return new UsdRate(rate, response.Date);
    }
}
