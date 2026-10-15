using ExpenseTracker.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Services;

/// <summary>
/// There's no sign-up page: the owner's account comes from configuration
/// (<c>Owner:Email</c>, <c>Owner:Password</c>: locally in appsettings.Development.json, which git
/// keeps blank via skip-worktree; app settings in Azure).
/// </summary>
public static class OwnerAccount
{
    public static async Task EnsureAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(OwnerAccount));
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

        var email = config["Owner:Email"];
        if (string.IsNullOrWhiteSpace(email))
        {
            log.LogWarning("Owner:Email isn't configured, so nobody can sign in.");
            return;
        }

        var owner = await users.FindByEmailAsync(email);
        if (owner is null)
        {
            var password = config["Owner:Password"];
            if (string.IsNullOrWhiteSpace(password))
            {
                log.LogWarning("Owner:Password isn't configured, so the account for {Email} wasn't created.", email);
                return;
            }

            owner = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            var result = await users.CreateAsync(owner, password);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    "Couldn't create the owner account: " + string.Join(" ", result.Errors.Select(e => e.Description)));
            log.LogInformation("Created the owner account for {Email}.", email);
        }

        await ClaimUnownedDataAsync(scope.ServiceProvider, owner.Id, log);
    }

    /// <summary>
    /// Rows without an owner (data from before sign-in existed, and seed data added by migrations)
    /// are given to the owner.
    /// </summary>
    private static async Task ClaimUnownedDataAsync(IServiceProvider services, string ownerId, ILogger log)
    {
        using var db = await services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();

        var claimed =
            await db.Categories.IgnoreQueryFilters().Where(x => x.UserId == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.UserId, ownerId)) +
            await db.Accounts.IgnoreQueryFilters().Where(x => x.UserId == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.UserId, ownerId)) +
            await db.RecurringExpenses.IgnoreQueryFilters().Where(x => x.UserId == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.UserId, ownerId)) +
            await db.Expenses.IgnoreQueryFilters().Where(x => x.UserId == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.UserId, ownerId));

        // At most one settings row per user: if the owner has none, claim the oldest unowned one.
        // Any other unowned rows are leftovers and are removed.
        var ownerHasSettings = await db.Settings.IgnoreQueryFilters().AnyAsync(x => x.UserId == ownerId);
        if (!ownerHasSettings)
        {
            var firstUnownedId = await db.Settings.IgnoreQueryFilters()
                .Where(x => x.UserId == null).OrderBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync();
            if (firstUnownedId is { } id)
                claimed += await db.Settings.IgnoreQueryFilters()
                    .Where(x => x.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserId, ownerId));
        }
        await db.Settings.IgnoreQueryFilters().Where(x => x.UserId == null).ExecuteDeleteAsync();

        if (claimed > 0)
            log.LogInformation("Gave {Count} unowned row(s) to the owner.", claimed);
    }
}
