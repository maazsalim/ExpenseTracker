using System.Reflection;
using ExpenseTracker.Models;
using ExpenseTracker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ExpenseTracker.Tests;

/// <summary>The public demo account: sample data, daily reset, and never touching anyone else's data.</summary>
public class DemoDataTests
{
    private const string Owner = "user-owner";
    private const string DemoUser = "user-demo";

    private static readonly DateOnly Today = new(2026, 10, 19);

    private readonly TestDb db = new();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero));
    private readonly DemoAccount demo = new();

    public DemoDataTests()
    {
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        // EnsureAsync needs Identity; here just set the demo user's ID directly.
        typeof(DemoAccount).GetProperty(nameof(DemoAccount.UserId))!.SetValue(demo, DemoUser);

        // The owner's real data.
        using var ctx = db.As(Owner);
        var category = new Category { Name = "Groceries" };
        ctx.Categories.Add(category);
        ctx.SaveChanges();
        ctx.Expenses.Add(new Expense { Description = "Owner's real expense", Amount = 50m, CategoryId = category.Id, Date = Today });
        ctx.RecurringExpenses.Add(new RecurringExpense { Description = "Owner's rent", Amount = 5000m, CategoryId = category.Id, NextDate = Today.AddDays(5) });
        ctx.Settings.Add(new AppSettings { TotalMonthlyBudget = 600m });
        ctx.SaveChanges();
    }

    private DemoDataService ServiceFor(string user) =>
        new(demo, new FixedUser(user), db.FactoryFor(user), new SavedRateFx(), clock, NullLogger<DemoDataService>.Instance);

    [Fact]
    public async Task LoadingSampleData_NeverTouchesTheOwnersData()
    {
        await ServiceFor(DemoUser).ResetIfDueAsync();
        await ServiceFor(DemoUser).ResetIfDueAsync(); // and again, with demo data already there

        using var owner = db.As(Owner);
        Assert.Equal("Owner's real expense", Assert.Single(owner.Expenses).Description);
        Assert.Equal("Owner's rent", Assert.Single(owner.RecurringExpenses).Description);
        Assert.Equal("Groceries", Assert.Single(owner.Categories).Name);
        Assert.Equal(600m, Assert.Single(owner.Settings).TotalMonthlyBudget);
    }

    [Fact]
    public async Task SampleData_HasRealisticContent()
    {
        await ServiceFor(DemoUser).ResetIfDueAsync();

        using var ctx = db.As(DemoUser);
        var expenses = ctx.Expenses.ToList();
        Assert.True(expenses.Count > 40);
        Assert.Contains(expenses, e => e.Currency == Currencies.AED);
        Assert.Contains(expenses, e => e.Currency == Currencies.CAD);
        Assert.All(expenses, e => Assert.True(e.AmountInHome > 0 && e.AmountInSecondary > 0));
        Assert.Single(expenses, e => e.Date > Today); // the booked flight: pending
        Assert.Contains(expenses, e => e.RecurringExpenseId != null); // entries from recurring items

        var recurring = ctx.RecurringExpenses.ToList();
        Assert.Equal(6, recurring.Count);
        Assert.All(recurring, r => Assert.True(r.NextDate > Today)); // nothing due again until later
        Assert.Equal(2, ctx.Accounts.Count());
        Assert.Equal(10, ctx.Categories.Count());
        Assert.Equal(6000m, ctx.Settings.Single().TotalMonthlyBudget);
    }

    [Fact]
    public async Task FreshDemoData_IsKept_StaleDemoData_IsReloaded()
    {
        var service = ServiceFor(DemoUser);
        await service.ResetIfDueAsync();
        using (var ctx = db.As(DemoUser))
        {
            // A visitor adds junk.
            ctx.Expenses.Add(new Expense { Description = "junk", Amount = 1m, CategoryId = ctx.Categories.First().Id, Date = Today });
            ctx.SaveChanges();
        }
        bool JunkThere() { using var ctx = db.As(DemoUser); return ctx.Expenses.Any(e => e.Description == "junk"); }

        clock.Advance(TimeSpan.FromHours(23));
        await service.ResetIfDueAsync();
        Assert.True(JunkThere()); // less than a day: kept

        clock.Advance(TimeSpan.FromHours(2));
        await service.ResetIfDueAsync();
        Assert.False(JunkThere()); // over a day: reloaded
    }

    [Fact]
    public async Task ForAnyoneButTheDemoUser_NothingHappens()
    {
        await ServiceFor(Owner).ResetIfDueAsync();

        using var owner = db.As(Owner);
        Assert.Equal("Owner's real expense", Assert.Single(owner.Expenses).Description);
        using var demoCtx = db.As(DemoUser);
        Assert.Empty(demoCtx.Expenses);
    }

    private sealed record FixedUser(string? UserId) : ICurrentUser;

    private class SavedRateFx : IExchangeRateService
    {
        public Task<FxQuote> GetRateAsync(string from, string to, DateOnly date, CancellationToken ct = default) =>
            throw new InvalidOperationException("Sample data must not call the rate service.");

        public Task<FxQuote?> GetLatestSavedRateAsync(string from, string to, CancellationToken ct = default) =>
            Task.FromResult<FxQuote?>(new FxQuote(0.3876m, Today, "saved"));
    }
}
