namespace ExpenseTracker.Services;

/// <summary>1 unit of the "from" currency = <see cref="Rate"/> units of the "to" currency.</summary>
public record FxQuote(decimal Rate, DateOnly RateDate, string Source);

public interface IExchangeRateService
{
    /// <summary>The rate from <paramref name="from"/> to <paramref name="to"/> on <paramref name="date"/>.</summary>
    /// <exception cref="ExchangeRateUnavailableException">No rate could be found.</exception>
    Task<FxQuote> GetRateAsync(string from, string to, DateOnly date, CancellationToken ct = default);
}

public class ExchangeRateUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
