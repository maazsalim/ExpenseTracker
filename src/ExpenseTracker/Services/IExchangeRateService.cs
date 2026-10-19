namespace ExpenseTracker.Services;

/// <summary>1 unit of the "from" currency = <see cref="Rate"/> units of the "to" currency.</summary>
public record FxQuote(decimal Rate, DateOnly RateDate, string Source);

public interface IExchangeRateService
{
    /// <summary>The rate from <paramref name="from"/> to <paramref name="to"/> on <paramref name="date"/>.</summary>
    /// <exception cref="ExchangeRateUnavailableException">No rate could be found.</exception>
    Task<FxQuote> GetRateAsync(string from, string to, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// The most recent rate already saved, without calling the rate service (so it's instant).
    /// For estimates such as pending amounts. Null if no rate has been saved yet.
    /// </summary>
    Task<FxQuote?> GetLatestSavedRateAsync(string from, string to, CancellationToken ct = default) =>
        Task.FromResult<FxQuote?>(null);
}

public class ExchangeRateUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
