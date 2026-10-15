using ExpenseTracker.Models;
using ExpenseTracker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExpenseTracker.Tests;

/// <summary>Proves one user can never see or change another user's data.</summary>
public class UserIsolationTests
{
    private const string Alice = "user-alice";
    private const string Bob = "user-bob";

    private readonly TestDb db = new();

    public UserIsolationTests()
    {
        // Each user adds one of everything.
        foreach (var user in new[] { Alice, Bob })
        {
            using var ctx = db.As(user);
            var category = new Category { Name = "Groceries" }; // same name for both: allowed
            var account = new Account { Name = "Card" };
            ctx.AddRange(category, account);
            ctx.SaveChanges();

            ctx.Expenses.Add(new Expense { Description = $"{user} lunch", Amount = 10m, CategoryId = category.Id, AccountId = account.Id });
            ctx.RecurringExpenses.Add(new RecurringExpense { Description = $"{user} rent", Amount = 100m, CategoryId = category.Id, NextDate = new DateOnly(2099, 1, 1) });
            ctx.Settings.Add(new AppSettings { TotalMonthlyBudget = user == Alice ? 500m : 900m });
            ctx.SaveChanges();
        }
    }

    [Fact]
    public void EachUser_SeesOnlyTheirOwnData()
    {
        using var alice = db.As(Alice);

        Assert.Equal($"{Alice} lunch", Assert.Single(alice.Expenses).Description);
        Assert.Equal($"{Alice} rent", Assert.Single(alice.RecurringExpenses).Description);
        Assert.Single(alice.Categories);
        Assert.Single(alice.Accounts);
        Assert.Equal(500m, Assert.Single(alice.Settings).TotalMonthlyBudget);
    }

    [Fact]
    public void Navigations_AreFilteredToo()
    {
        using var alice = db.As(Alice);

        var expenseCounts = alice.Categories.Select(c => c.Expenses.Count).ToList();

        Assert.Equal([1], expenseCounts);
    }

    [Fact]
    public void AnotherUsersRow_CantBeFoundById()
    {
        int bobsExpenseId;
        using (var bob = db.As(Bob))
            bobsExpenseId = bob.Expenses.Single().Id;

        using var alice = db.As(Alice);
        Assert.Null(alice.Expenses.Find(bobsExpenseId));
        Assert.Null(alice.Expenses.FirstOrDefault(e => e.Id == bobsExpenseId));
    }

    [Fact]
    public void AnotherUsersRow_CantBeChangedOrDeleted()
    {
        Expense bobsExpense;
        using (var bob = db.As(Bob))
            bobsExpense = bob.Expenses.AsNoTracking().Single();

        using var alice = db.As(Alice);

        bobsExpense.Description = "changed by Alice";
        alice.Expenses.Update(bobsExpense);
        Assert.Throws<InvalidOperationException>(() => alice.SaveChanges());

        alice.ChangeTracker.Clear();
        alice.Expenses.Remove(bobsExpense);
        Assert.Throws<InvalidOperationException>(() => alice.SaveChanges());

        using var check = db.As(Bob);
        Assert.Equal($"{Bob} lunch", check.Expenses.Single().Description);
    }

    [Fact]
    public void NewRows_BelongToWhoeverAddedThem()
    {
        using (var alice = db.As(Alice))
        {
            alice.Categories.Add(new Category { Name = "Travel" });
            alice.SaveChanges();
        }

        using var bob = db.As(Bob);
        Assert.DoesNotContain(bob.Categories, c => c.Name == "Travel");
        Assert.Equal(Alice, bob.Categories.IgnoreQueryFilters().Single(c => c.Name == "Travel").UserId);
    }

    [Fact]
    public void NobodySignedIn_SeesNothing_AndCantSave()
    {
        using var anonymous = db.As(null);

        Assert.Empty(anonymous.Expenses);
        Assert.Empty(anonymous.Categories);
        Assert.Empty(anonymous.Accounts);
        Assert.Empty(anonymous.RecurringExpenses);
        Assert.Empty(anonymous.Settings);

        anonymous.Categories.Add(new Category { Name = "Sneaky" });
        Assert.Throws<InvalidOperationException>(() => anonymous.SaveChanges());
    }

    [Fact]
    public async Task RecurringItems_OnlyCreateExpensesForTheirOwner()
    {
        // Make both users' items due.
        foreach (var user in new[] { Alice, Bob })
        {
            using var ctx = db.As(user);
            ctx.RecurringExpenses.Single().NextDate = DateOnly.FromDateTime(DateTime.Today);
            ctx.SaveChanges();
        }

        var service = new RecurringExpenseService(
            db.FactoryFor(Alice),
            new ExpenseConverter(new SameRateFx(), Options.Create(new CurrencyOptions())),
            TimeProvider.System,
            NullLogger<RecurringExpenseService>.Instance);

        Assert.Equal(1, await service.CreateDueAsync());

        using var bob = db.As(Bob);
        Assert.Single(bob.Expenses); // Bob's item wasn't touched by Alice's run
    }

    private class SameRateFx : IExchangeRateService
    {
        public Task<FxQuote> GetRateAsync(string from, string to, DateOnly date, CancellationToken ct = default) =>
            Task.FromResult(new FxQuote(1m, date, "test"));
    }
}
