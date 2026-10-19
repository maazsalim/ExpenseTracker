using ExpenseTracker.Models;
using ExpenseTracker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ExpenseTracker.Tests;

public class PendingExpenseServiceTests
{
    private const string Me = "user-me";
    private const string Someone = "user-someone";

    // "Today" is 15 Oct 2026; October runs to the 31st.
    private static readonly DateOnly Today = new(2026, 10, 15);
    private static readonly DateOnly MonthStart = new(2026, 10, 1);
    private static readonly DateOnly MonthEnd = new(2026, 10, 31);

    private readonly TestDb db = new();
    private readonly FakeFx fx = new();

    private PendingExpenseService ServiceFor(string user)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(Today.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        return new PendingExpenseService(db.FactoryFor(user), fx,
            Options.Create(new CurrencyOptions { HomeCurrency = Currencies.CAD, SecondaryCurrency = Currencies.AED }),
            clock, NullLogger<PendingExpenseService>.Instance);
    }

    private void AddExpense(string user, DateOnly date, decimal cad, decimal aed)
    {
        using var ctx = db.As(user);
        ctx.Expenses.Add(new Expense { Description = $"expense {date}", Date = date, CategoryId = 1, AmountInHome = cad, AmountInSecondary = aed });
        ctx.SaveChanges();
    }

    private void AddRecurring(string user, decimal amount, string currency, int day, DateOnly nextDate, bool active = true)
    {
        using var ctx = db.As(user);
        ctx.RecurringExpenses.Add(new RecurringExpense
        {
            Description = "Rent", Amount = amount, Currency = currency, CategoryId = 2,
            DayOfMonth = day, StartDate = nextDate, NextDate = nextDate, Active = active,
        });
        ctx.SaveChanges();
    }

    [Fact]
    public async Task ExpensesDatedAfterToday_ArePending_TodaysAreNot()
    {
        AddExpense(Me, Today, 10m, 25m);
        AddExpense(Me, Today.AddDays(3), 40m, 100m);

        var pending = await ServiceFor(Me).BetweenAsync(MonthStart, MonthEnd);

        var entry = Assert.Single(pending);
        Assert.Equal((Today.AddDays(3), 40m, 100m), (entry.Date, entry.AmountInHome, entry.AmountInSecondary));
    }

    [Fact]
    public async Task RecurringItemsDueLaterThisMonth_ArePending_AtTheLatestSavedRate()
    {
        AddRecurring(Me, 5000m, Currencies.AED, day: 28, nextDate: new DateOnly(2026, 10, 28));

        var entry = Assert.Single(await ServiceFor(Me).BetweenAsync(MonthStart, MonthEnd));

        Assert.Equal(new DateOnly(2026, 10, 28), entry.Date);
        Assert.Equal(2, entry.CategoryId);
        Assert.Equal((1940m, 5000m), (entry.AmountInHome, entry.AmountInSecondary)); // 5000 × 0.388
    }

    [Fact]
    public async Task ARecurringDateThatAlreadyHasItsEntry_IsCountedOnce()
    {
        // The gym's next date (21 Oct) is also the date of its existing, later-dated entry.
        var gymDate = new DateOnly(2026, 10, 21);
        AddRecurring(Me, 300m, Currencies.AED, day: 21, nextDate: gymDate);
        using (var ctx = db.As(Me))
        {
            var itemId = ctx.RecurringExpenses.Single().Id;
            ctx.Expenses.Add(new Expense
            {
                Description = "Gym", Date = gymDate, CategoryId = 2, RecurringExpenseId = itemId,
                AmountInHome = 116.15m, AmountInSecondary = 300m,
            });
            ctx.SaveChanges();
        }

        var entry = Assert.Single(await ServiceFor(Me).BetweenAsync(MonthStart, MonthEnd));

        Assert.Equal((gymDate, 116.15m), (entry.Date, entry.AmountInHome)); // the real entry, not an estimate
    }

    [Fact]
    public async Task NextMonthsDates_AreNotPendingThisMonth()
    {
        AddRecurring(Me, 50m, Currencies.CAD, day: 5, nextDate: new DateOnly(2026, 11, 5));

        Assert.Empty(await ServiceFor(Me).BetweenAsync(MonthStart, MonthEnd));
    }

    [Fact]
    public async Task OverdueItemsNotAddedYet_ArePending()
    {
        // Due on the 1st but not added yet (e.g. no rate was available then).
        AddRecurring(Me, 50m, Currencies.CAD, day: 1, nextDate: MonthStart);

        var entry = Assert.Single(await ServiceFor(Me).BetweenAsync(DateOnly.MinValue, MonthEnd));

        Assert.Equal(MonthStart, entry.Date);
        Assert.Equal((50m, 128.85m), (entry.AmountInHome, entry.AmountInSecondary)); // 50 × 2.577
    }

    [Fact]
    public async Task PausedItems_AreNotPending()
    {
        AddRecurring(Me, 50m, Currencies.CAD, day: 28, nextDate: new DateOnly(2026, 10, 28), active: false);

        Assert.Empty(await ServiceFor(Me).BetweenAsync(MonthStart, MonthEnd));
    }

    [Fact]
    public async Task WithNoSavedRate_ItemsInTheOtherCurrencyAreLeftOut()
    {
        fx.HasSavedRates = false;
        AddRecurring(Me, 5000m, Currencies.AED, day: 28, nextDate: new DateOnly(2026, 10, 28));

        Assert.Empty(await ServiceFor(Me).BetweenAsync(MonthStart, MonthEnd));
    }

    [Fact]
    public async Task OnlyTheSignedInUsersItemsArePending()
    {
        AddExpense(Someone, Today.AddDays(2), 99m, 99m);
        AddRecurring(Someone, 99m, Currencies.CAD, day: 28, nextDate: new DateOnly(2026, 10, 28));

        Assert.Empty(await ServiceFor(Me).BetweenAsync(MonthStart, MonthEnd));
    }

    private class FakeFx : IExchangeRateService
    {
        public bool HasSavedRates { get; set; } = true;

        public Task<FxQuote> GetRateAsync(string from, string to, DateOnly date, CancellationToken ct = default) =>
            throw new InvalidOperationException("Pending amounts must not call the rate service.");

        public Task<FxQuote?> GetLatestSavedRateAsync(string from, string to, CancellationToken ct = default) =>
            Task.FromResult<FxQuote?>((from, to) switch
            {
                _ when from == to => new FxQuote(1m, Today, "same-currency"),
                _ when !HasSavedRates => null,
                (Currencies.AED, Currencies.CAD) => new FxQuote(0.388m, Today, "saved"),
                (Currencies.CAD, Currencies.AED) => new FxQuote(2.577m, Today, "saved"),
                _ => null,
            });
    }
}
