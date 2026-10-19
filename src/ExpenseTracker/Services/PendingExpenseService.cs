using ExpenseTracker.Data;
using ExpenseTracker.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExpenseTracker.Services;

/// <summary>
/// An expense that's expected but not spent yet. Amounts are in both the home and secondary
/// currency, like an <see cref="Expense"/>.
/// </summary>
public record PendingEntry(DateOnly Date, string Description, int CategoryId, decimal AmountInHome, decimal AmountInSecondary);

/// <summary>
/// Works out what's pending: expenses dated after today, and recurring items' due dates that
/// haven't been added yet. Recurring amounts are estimates, converted at the latest saved rate
/// (no call to the rate service, so pages never wait for it).
/// </summary>
public class PendingExpenseService(
    IDbContextFactory<AppDbContext> dbFactory,
    IExchangeRateService fx,
    IOptions<CurrencyOptions> options,
    TimeProvider time,
    ILogger<PendingExpenseService> log)
{
    public DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    /// <summary>Pending entries dated <paramref name="from"/> to <paramref name="to"/> (inclusive).</summary>
    public async Task<List<PendingEntry>> BetweenAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var today = Today;
        using var db = await dbFactory.CreateDbContextAsync(ct);

        // Expenses entered with a later date: already converted when they were saved.
        var pending = await db.Expenses
            .Where(e => e.Date > today && e.Date >= from && e.Date <= to)
            .Select(e => new PendingEntry(e.Date, e.Description, e.CategoryId, e.AmountInHome, e.AmountInSecondary))
            .ToListAsync(ct);

        // Recurring items: every due date up to `to` that hasn't been added yet (NextDate onwards).
        var items = await db.RecurringExpenses
            .Where(r => r.Active && r.NextDate <= to)
            .ToListAsync(ct);

        // Dates that already have their entry (an expense linked to the item), so they aren't
        // counted twice: once as that expense and once as the recurring item.
        var itemIds = items.Select(i => i.Id).ToList();
        var alreadyAdded = (await db.Expenses
                .Where(e => e.RecurringExpenseId != null && itemIds.Contains(e.RecurringExpenseId.Value) && e.Date <= to)
                .Select(e => new { Item = e.RecurringExpenseId!.Value, e.Date })
                .ToListAsync(ct))
            .Select(e => (e.Item, e.Date))
            .ToHashSet();

        var rates = new Dictionary<string, (decimal ToHome, decimal ToSecondary)?>();
        foreach (var item in items)
        {
            if (!rates.TryGetValue(item.Currency, out var rate))
                rates[item.Currency] = rate = await RatesForAsync(item.Currency, ct);
            if (rate is not { } r)
            {
                log.LogWarning("No saved {Currency} rate yet; leaving recurring item {Id} out of pending totals", item.Currency, item.Id);
                continue;
            }

            for (var date = item.NextDate; date <= to; date = MonthlySchedule.Next(date, item.DayOfMonth))
            {
                if (date >= from && !alreadyAdded.Contains((item.Id, date)))
                    pending.Add(new PendingEntry(date, item.Description, item.CategoryId,
                        Money.Convert(item.Amount, r.ToHome), Money.Convert(item.Amount, r.ToSecondary)));
            }
        }

        return pending;
    }

    private async Task<(decimal ToHome, decimal ToSecondary)?> RatesForAsync(string currency, CancellationToken ct)
    {
        var toHome = await fx.GetLatestSavedRateAsync(currency, options.Value.HomeCurrency, ct);
        var toSecondary = await fx.GetLatestSavedRateAsync(currency, options.Value.SecondaryCurrency, ct);
        return toHome is null || toSecondary is null ? null : (toHome.Rate, toSecondary.Rate);
    }
}
