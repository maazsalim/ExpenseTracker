namespace ExpenseTracker.Services;

/// <summary>A published rate: 1 USD = <see cref="Rate"/> units of the currency, as of <see cref="RateDate"/>.</summary>
public record UsdRate(decimal Rate, DateOnly RateDate);

/// <summary>Where USD rates come from (a web API in production, a fake in tests).</summary>
public interface IUsdRateSource
{
    string Name { get; }

    /// <summary>
    /// The rate for <paramref name="currency"/> on <paramref name="date"/>, or the most recent
    /// earlier one if nothing was published that day.
    /// </summary>
    Task<UsdRate> GetAsync(string currency, DateOnly date, CancellationToken ct = default);
}
