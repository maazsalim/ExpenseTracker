using Microsoft.AspNetCore.Identity;

namespace ExpenseTracker.Services;

/// <summary>
/// The public demo account (for portfolio visitors), configured under <c>Demo:Email</c> and
/// <c>Demo:Password</c>. Its data is fake sample data, separate from everyone else's through the
/// per-user filter, and reloaded once a day (<see cref="DemoDataService"/>).
/// </summary>
public class DemoAccount
{
    /// <summary>The demo user's ID, once <see cref="EnsureAsync"/> has run; null if there's no demo.</summary>
    public string? UserId { get; private set; }

    public string? Email { get; private set; }

    public bool IsDemoUser(string? userId) => userId is not null && userId == UserId;

    /// <summary>Creates the demo user if needed. Run at startup.</summary>
    public static async Task EnsureAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var demo = services.GetRequiredService<DemoAccount>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<DemoAccount>();

        var email = config["Demo:Email"];
        var password = config["Demo:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return; // no demo account

        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                log.LogError("Couldn't create the demo account: {Errors}", string.Join(" ", result.Errors.Select(e => e.Description)));
                return;
            }
            log.LogInformation("Created the demo account {Email}.", email);
        }

        // Its password is public, so wrong guesses mustn't lock visitors out.
        if (user.LockoutEnabled)
            await users.SetLockoutEnabledAsync(user, false);

        demo.UserId = user.Id;
        demo.Email = email;
    }
}
