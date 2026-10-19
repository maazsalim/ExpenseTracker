using ExpenseTracker.Data;
using ExpenseTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Services;

/// <summary>
/// Converts between currencies via USD: every rate is "units per 1 USD", AED comes from the
/// peg, and other currencies come from the cache or the <see cref="IUsdRateSource"/>.
/// </summary>
public class ExchangeRateService(
    IDbContextFactory<AppDbContext> dbFactory,
    IUsdRateSource source,
    TimeProvider time) : IExchangeRateService
{
    // Enough precision for a rate; matches the numeric(18,8) columns.
    private const int RateDecimals = 8;

    // How old a saved rate may be when the rate service can't be reached.
    private const int MaxFallbackAgeDays = 7;

    public async Task<FxQuote> GetRateAsync(string from, string to, DateOnly date, CancellationToken ct = default)
    {
        if (from == to)
            return new FxQuote(1m, date, "same-currency");

        // There are no rates for the future yet; use today's.
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        if (date > today)
            date = today;

        var fromUsd = await PerUsdAsync(from, date, today, ct);
        var toUsd = await PerUsdAsync(to, date, today, ct);

        var rate = Math.Round(toUsd.Rate / fromUsd.Rate, RateDecimals);
        // If the two legs were published on different days, report the older one.
        var rateDate = fromUsd.RateDate < toUsd.RateDate ? fromUsd.RateDate : toUsd.RateDate;
        var sources = new[] { fromUsd.Source, toUsd.Source }.Where(s => s != "").Distinct();

        return new FxQuote(rate, rateDate, string.Join("+", sources));
    }

    public async Task<FxQuote?> GetLatestSavedRateAsync(string from, string to, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        if (from == to)
            return new FxQuote(1m, today, "same-currency");

        using var db = await dbFactory.CreateDbContextAsync(ct);

        async Task<(decimal Rate, DateOnly RateDate)?> LatestPerUsd(string currency)
        {
            switch (currency)
            {
                case Currencies.USD:
                    return (1m, today);
                case Currencies.AED:
                    return (Currencies.AedPerUsd, today);
            }
            var row = await db.ExchangeRates
                .Where(r => r.Base == Currencies.USD && r.Quote == currency)
                .OrderByDescending(r => r.Date)
                .FirstOrDefaultAsync(ct);
            return row is null ? null : (row.Rate, row.RateDate);
        }

        if (await LatestPerUsd(from) is not { } fromUsd || await LatestPerUsd(to) is not { } toUsd)
            return null;

        var rateDate = fromUsd.RateDate < toUsd.RateDate ? fromUsd.RateDate : toUsd.RateDate;
        return new FxQuote(Math.Round(toUsd.Rate / fromUsd.Rate, RateDecimals), rateDate, "saved");
    }

    private async Task<(decimal Rate, DateOnly RateDate, string Source)> PerUsdAsync(
        string currency, DateOnly date, DateOnly today, CancellationToken ct)
    {
        switch (currency)
        {
            case Currencies.USD:
                return (1m, date, "");
            case Currencies.AED:
                return (Currencies.AedPerUsd, date, "peg");
        }

        using var db = await dbFactory.CreateDbContextAsync(ct);
        var cached = await db.ExchangeRates.FirstOrDefaultAsync(
            r => r.Date == date && r.Base == Currencies.USD && r.Quote == currency, ct);
        if (cached is not null)
            return (cached.Rate, cached.RateDate, cached.Source);

        UsdRate fetched;
        try
        {
            fetched = await source.GetAsync(currency, date, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // The rate service is down or slow: use the most recent rate already saved, if it's
            // at most a week old. Its RateDate records which day's rate was actually used.
            var recent = await db.ExchangeRates
                .Where(r => r.Base == Currencies.USD && r.Quote == currency
                            && r.Date <= date && r.Date >= date.AddDays(-MaxFallbackAgeDays))
                .OrderByDescending(r => r.Date)
                .FirstOrDefaultAsync(ct);
            if (recent is not null)
                return (recent.Rate, recent.RateDate, recent.Source);

            throw new ExchangeRateUnavailableException($"Couldn't get the {currency} rate for {date:d MMM yyyy}.", ex);
        }

        // Today's rate may not be published yet, so only cache days that are over.
        if (date < today)
        {
            db.ExchangeRates.Add(new ExchangeRate
            {
                Date = date,
                RateDate = fetched.RateDate,
                Base = Currencies.USD,
                Quote = currency,
                Rate = fetched.Rate,
                Source = source.Name,
                FetchedAt = time.GetUtcNow().UtcDateTime,
            });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Another request cached the same day first; that's fine.
            }
        }

        return (fetched.Rate, fetched.RateDate, source.Name);
    }
}
