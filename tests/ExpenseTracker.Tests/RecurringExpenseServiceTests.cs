using ExpenseTracker.Data;
using ExpenseTracker.Models;
using ExpenseTracker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ExpenseTracker.Tests;

public class RecurringExpenseServiceTests
{
    // "Today" is 14 Oct 2026.
    private static readonly DateOnly Today = new(2026, 10, 14);

    private const string Me = "user-me";

    private readonly TestDb testDb = new();
    private readonly IDbContextFactory<AppDbContext> dbFactory;
    private readonly FakeFx fx = new();
    private readonly RecurringExpenseService service;

    public RecurringExpenseServiceTests()
    {
        dbFactory = testDb.FactoryFor(Me);
        var clock = new FakeTimeProvider(new DateTimeOffset(Today.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        var converter = new ExpenseConverter(fx,
            Options.Create(new CurrencyOptions { HomeCurrency = Currencies.CAD, SecondaryCurrency = Currencies.AED }));
        service = new RecurringExpenseService(dbFactory, converter, clock, NullLogger<RecurringExpenseService>.Instance);
    }

    private int Add(RecurringExpense item)
    {
        using var db = dbFactory.CreateDbContext();
        item.NextDate = MonthlySchedule.FirstOnOrAfter(item.StartDate, item.DayOfMonth);
        db.RecurringExpenses.Add(item);
        db.SaveChanges();
        return item.Id;
    }

    private static RecurringExpense Rent(DateOnly start, int day = 1) => new()
    {
        Description = "Rent", Amount = 5000m, Currency = Currencies.AED, CategoryId = 1, AccountId = 7,
        DayOfMonth = day, StartDate = start,
    };

    [Fact]
    public async Task CatchesUpOnMissedMonths_WithConvertedAmounts()
    {
        var id = Add(Rent(new DateOnly(2026, 8, 1)));

        var created = await service.CreateDueAsync();

        Assert.Equal(3, created); // 1 Aug, 1 Sep, 1 Oct
        using var db = dbFactory.CreateDbContext();
        var expenses = db.Expenses.OrderBy(e => e.Date).ToList();
        Assert.Equal(
            [new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)],
            expenses.Select(e => e.Date));
        Assert.All(expenses, e =>
        {
            Assert.Equal(("Rent", 5000m, "AED", 1, 7, id), (e.Description, e.Amount, e.Currency, e.CategoryId, e.AccountId, e.RecurringExpenseId));
            Assert.Equal((1940m, 5000m), (e.AmountInHome, e.AmountInSecondary)); // 5000 × 0.388
        });
        Assert.Equal(new DateOnly(2026, 11, 1), db.RecurringExpenses.Single().NextDate);
    }

    [Fact]
    public async Task RunningAgain_CreatesNothingNew()
    {
        Add(Rent(new DateOnly(2026, 9, 1)));

        await service.CreateDueAsync();
        var second = await service.CreateDueAsync();

        Assert.Equal(0, second);
        using var db = dbFactory.CreateDbContext();
        Assert.Equal(2, db.Expenses.Count());
    }

    [Fact]
    public async Task NotDueYet_CreatesNothing()
    {
        Add(Rent(Today, day: 20));

        Assert.Equal(0, await service.CreateDueAsync());
    }

    [Fact]
    public async Task DueToday_IsCreated()
    {
        Add(Rent(Today, day: 14));

        Assert.Equal(1, await service.CreateDueAsync());
    }

    [Fact]
    public async Task PausedItems_AreSkipped()
    {
        var item = Rent(new DateOnly(2026, 9, 1));
        item.Active = false;
        Add(item);

        Assert.Equal(0, await service.CreateDueAsync());
    }

    [Fact]
    public async Task NoRate_LeavesItDueForNextTime()
    {
        Add(Rent(new DateOnly(2026, 10, 1)));
        fx.Fail = true;

        Assert.Equal(0, await service.CreateDueAsync());
        using (var db = dbFactory.CreateDbContext())
            Assert.Equal(new DateOnly(2026, 10, 1), db.RecurringExpenses.Single().NextDate);

        fx.Fail = false;
        Assert.Equal(1, await service.CreateDueAsync());
    }

    [Fact]
    public async Task ADueDateThatAlreadyHasItsEntry_IsSkipped_AndTheItemMovesOn()
    {
        // The schedule was edited onto a date that already has its entry (the 1 Oct rent).
        var id = Add(Rent(new DateOnly(2026, 10, 1)));
        using (var ctx = dbFactory.CreateDbContext())
        {
            ctx.Expenses.Add(new Expense
            {
                Description = "Rent", Amount = 5000m, Currency = Currencies.AED, CategoryId = 1,
                Date = new DateOnly(2026, 10, 1), RecurringExpenseId = id,
            });
            ctx.SaveChanges();
        }

        var created = await service.CreateDueAsync();

        Assert.Equal(0, created);
        using var check = dbFactory.CreateDbContext();
        Assert.Single(check.Expenses);
        Assert.Equal(new DateOnly(2026, 11, 1), check.RecurringExpenses.Single().NextDate);
    }

    [Fact]
    public void FromExpense_CopiesIt_AndIsNextDueAMonthLater()
    {
        var expense = new Expense
        {
            Description = "Netflix", Amount = 45m, Currency = Currencies.AED, CategoryId = 6, AccountId = 2,
            Date = new DateOnly(2026, 1, 31),
        };

        var item = RecurringExpenseService.FromExpense(expense);

        Assert.Equal(("Netflix", 45m, "AED", 6, 2), (item.Description, item.Amount, item.Currency, item.CategoryId, item.AccountId));
        Assert.Equal(31, item.DayOfMonth);
        Assert.Equal(new DateOnly(2026, 1, 31), item.StartDate);
        Assert.Equal(new DateOnly(2026, 2, 28), item.NextDate); // short month: last day
        Assert.True(item.Active);
    }

    [Fact]
    public async Task MakingAnOldExpenseRecurring_AddsTheMonthsSince()
    {
        using (var db = dbFactory.CreateDbContext())
        {
            var expense = new Expense
            {
                Description = "Gym", Amount = 300m, Currency = Currencies.AED, CategoryId = 1,
                Date = new DateOnly(2026, 8, 20),
            };
            expense.RecurringExpense = RecurringExpenseService.FromExpense(expense);
            db.Expenses.Add(expense);
            db.SaveChanges();
        }

        var created = await service.CreateDueAsync();

        Assert.Equal(1, created); // 20 Sep; 20 Oct isn't due until after the 14th
        using var check = dbFactory.CreateDbContext();
        var itemId = check.RecurringExpenses.Single().Id;
        Assert.Equal(
            [new DateOnly(2026, 8, 20), new DateOnly(2026, 9, 20)],
            check.Expenses.Where(e => e.RecurringExpenseId == itemId).OrderBy(e => e.Date).Select(e => e.Date));
    }

    private class FakeFx : IExchangeRateService
    {
        public bool Fail { get; set; }

        public Task<FxQuote> GetRateAsync(string from, string to, DateOnly date, CancellationToken ct = default)
        {
            if (from == to)
                return Task.FromResult(new FxQuote(1m, date, "same-currency"));
            if (Fail)
                throw new ExchangeRateUnavailableException("offline");
            return Task.FromResult(new FxQuote(0.388m, date, "test"));
        }
    }
}
