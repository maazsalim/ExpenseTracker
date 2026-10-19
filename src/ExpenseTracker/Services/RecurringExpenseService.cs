using ExpenseTracker.Data;
using ExpenseTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Services;

/// <summary>
/// Creates the expenses recurring items are due for. The free host sleeps, so there's no
/// timer: this runs when the app is opened (once per circuit) and catches up on missed months.
/// </summary>
public class RecurringExpenseService(
    IDbContextFactory<AppDbContext> dbFactory,
    ExpenseConverter converter,
    TimeProvider time,
    ILogger<RecurringExpenseService> log)
{
    private Task? run;

    /// <summary>Creates due entries the first time it's called in this circuit; later calls reuse that run.</summary>
    public Task RunOnceAsync() => run ??= RunSafelyAsync();

    private async Task RunSafelyAsync()
    {
        try
        {
            var created = await CreateDueAsync();
            if (created > 0)
                log.LogInformation("Created {Count} recurring expense(s)", created);
        }
        catch (Exception ex)
        {
            // Never block opening the app; it'll try again next time.
            log.LogError(ex, "Creating recurring expenses failed");
        }
    }

    /// <summary>
    /// A monthly recurring item copied from <paramref name="expense"/>, due on the same day each
    /// month. The expense itself counts as the first entry, so the next one is a month later.
    /// </summary>
    public static RecurringExpense FromExpense(Expense expense) => new()
    {
        Description = expense.Description,
        Amount = expense.Amount,
        Currency = expense.Currency,
        CategoryId = expense.CategoryId,
        AccountId = expense.AccountId,
        DayOfMonth = expense.Date.Day,
        StartDate = expense.Date,
        NextDate = MonthlySchedule.Next(expense.Date, expense.Date.Day),
    };

    /// <summary>Creates an expense for every due date up to today. Returns how many were created.</summary>
    public async Task<int> CreateDueAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var created = 0;

        using var db = await dbFactory.CreateDbContextAsync(ct);
        var due = await db.RecurringExpenses
            .Where(r => r.Active && r.NextDate <= today)
            .ToListAsync(ct);

        foreach (var item in due)
        {
            while (item.NextDate <= today)
            {
                // This date already has its entry (e.g. it was entered by hand, or the schedule
                // was edited onto it): just move on, otherwise the item would never advance.
                var dueDate = item.NextDate;
                if (await db.Expenses.AnyAsync(e => e.RecurringExpenseId == item.Id && e.Date == dueDate, ct))
                {
                    item.NextDate = MonthlySchedule.Next(item.NextDate, item.DayOfMonth);
                    await db.SaveChangesAsync(ct);
                    continue;
                }

                var expense = new Expense
                {
                    Description = item.Description,
                    Amount = item.Amount,
                    Currency = item.Currency,
                    CategoryId = item.CategoryId,
                    AccountId = item.AccountId,
                    Date = item.NextDate,
                    RecurringExpenseId = item.Id,
                };

                try
                {
                    await converter.ConvertAsync(expense, ct);
                }
                catch (ExchangeRateUnavailableException ex)
                {
                    // Leave it due; it'll be created next time the rate is available.
                    log.LogWarning(ex, "No rate for recurring item {Id} on {Date}; will retry", item.Id, item.NextDate);
                    break;
                }

                // Saving the expense and moving NextDate on happen together.
                db.Expenses.Add(expense);
                item.NextDate = MonthlySchedule.Next(item.NextDate, item.DayOfMonth);
                try
                {
                    await db.SaveChangesAsync(ct);
                    created++;
                }
                catch (DbUpdateException)
                {
                    // Another tab created this entry at the same moment; let it finish.
                    return created;
                }
            }
        }

        return created;
    }
}
