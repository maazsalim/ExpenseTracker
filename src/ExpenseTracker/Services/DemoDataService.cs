using ExpenseTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Services;

/// <summary>
/// Reloads the demo account's sample data when it's more than a day old (or missing). Like
/// recurring items, this happens when the app is opened, because the free host can't run timers.
/// Only ever acts for the demo user, through a context for that user.
/// </summary>
public class DemoDataService(
    DemoAccount demo,
    ICurrentUser currentUser,
    IDbContextFactory<AppDbContext> dbFactory,
    IExchangeRateService fx,
    TimeProvider time,
    ILogger<DemoDataService> log)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);

    // One reload at a time, even if several visitors open the demo at once (single server).
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private Task? run;

    /// <summary>Checks once per circuit (browser tab).</summary>
    public Task ResetIfDueOnceAsync() => run ??= ResetIfDueAsync();

    public async Task ResetIfDueAsync(CancellationToken ct = default)
    {
        if (!demo.IsDemoUser(currentUser.UserId))
            return;

        await Gate.WaitAsync(ct);
        try
        {
            using var db = await dbFactory.CreateDbContextAsync(ct); // filtered to the demo user
            var nowUtc = time.GetUtcNow().UtcDateTime;
            var loadedAt = (await db.Settings.SingleOrDefaultAsync(ct))?.SampleDataLoadedAt;
            if (loadedAt is { } at && nowUtc - at < MaxAge)
                return;

            await SampleData.LoadAsync(db, fx, DateOnly.FromDateTime(time.GetLocalNow().DateTime), nowUtc, ct);
            log.LogInformation("Reloaded the demo account's sample data.");
        }
        catch (Exception ex)
        {
            // Never block the page; it'll try again next time.
            log.LogError(ex, "Reloading the demo data failed");
        }
        finally
        {
            Gate.Release();
        }
    }
}
