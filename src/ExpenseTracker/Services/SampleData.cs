using ExpenseTracker.Data;
using ExpenseTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Services;

/// <summary>
/// Fake but realistic data for the demo account: life split between Dubai (AED) and Canada (CAD),
/// covering last month and this month up to today, with monthly recurring items and one upcoming
/// expense so pending amounts show. The same every time (fixed random seed), shifted to today.
/// </summary>
public static class SampleData
{
    // Used only if no real rate has been saved yet: 1 USD ≈ 1.38 CAD, and AED is pegged.
    private const decimal FallbackCadPerUsd = 1.38m;

    /// <summary>
    /// Replaces all of the signed-in user's data with sample data. <paramref name="db"/> must be a
    /// context for that user: the per-user filter then guarantees only their rows are touched.
    /// </summary>
    public static async Task LoadAsync(AppDbContext db, IExchangeRateService fx, DateOnly today, DateTime nowUtc, CancellationToken ct = default)
    {
        // Clear the user's data. The filter only loads this user's rows, and saving also refuses to
        // delete anyone else's, so other users' data can't be touched.
        db.Expenses.RemoveRange(await db.Expenses.ToListAsync(ct));
        db.RecurringExpenses.RemoveRange(await db.RecurringExpenses.ToListAsync(ct));
        db.Categories.RemoveRange(await db.Categories.ToListAsync(ct));
        db.Accounts.RemoveRange(await db.Accounts.ToListAsync(ct));
        db.Settings.RemoveRange(await db.Settings.ToListAsync(ct));
        await db.SaveChangesAsync(ct);

        var aedToCad = (await fx.GetLatestSavedRateAsync(Currencies.AED, Currencies.CAD, ct))?.Rate
                       ?? Math.Round(FallbackCadPerUsd / Currencies.AedPerUsd, 8);
        var cadToAed = Math.Round(1 / aedToCad, 8);

        // Categories and accounts.
        Category Cat(string name, string color, decimal? budget = null) => new() { Name = name, Color = color, MonthlyBudget = budget };
        var rent = Cat("Rent & Housing", "indigo");
        var groceries = Cat("Groceries", "emerald", 700m);
        var dining = Cat("Dining Out", "orange", 400m);
        var transport = Cat("Transport", "sky");
        var bills = Cat("Bills & Utilities", "amber");
        var subscriptions = Cat("Subscriptions", "violet");
        var health = Cat("Health & Fitness", "teal");
        var shopping = Cat("Shopping", "rose");
        var fun = Cat("Entertainment", "pink");
        var travel = Cat("Travel", "slate");
        db.Categories.AddRange(rent, groceries, dining, transport, bills, subscriptions, health, shopping, fun, travel);

        var aedBank = new Account { Name = "Emirates NBD (AED)", Currency = Currencies.AED };
        var cadCard = new Account { Name = "RBC Visa (CAD)", Currency = Currencies.CAD };
        db.Accounts.AddRange(aedBank, cadCard);

        db.Settings.Add(new AppSettings { TotalMonthlyBudget = 6000m, SampleDataLoadedAt = nowUtc });
        await db.SaveChangesAsync(ct); // assigns IDs

        var from = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);

        Expense NewExpense(string description, decimal amount, string currency, DateOnly date, Category category, Account account)
        {
            var expense = new Expense
            {
                Description = description, Amount = amount, Currency = currency, Date = date,
                CategoryId = category.Id, AccountId = account.Id, RateDate = date,
                FxRate = currency == Currencies.CAD ? 1m : aedToCad,
                SecondaryFxRate = currency == Currencies.AED ? 1m : cadToAed,
            };
            ExpenseConverter.ApplyRates(expense);
            return expense;
        }

        // Monthly recurring items, with their entries up to today and the next one scheduled.
        (string Name, decimal Amount, string Currency, int Day, Category Category, Account Account)[] monthly =
        [
            ("Rent", 6500m, Currencies.AED, 1, rent, aedBank),
            ("Netflix", 16.49m, Currencies.CAD, 5, subscriptions, cadCard),
            ("DEWA (electricity & water)", 420m, Currencies.AED, 10, bills, aedBank),
            ("du mobile plan", 175m, Currencies.AED, 15, bills, aedBank),
            ("Spotify", 11.99m, Currencies.CAD, 22, subscriptions, cadCard),
            ("Gym membership", 300m, Currencies.AED, 25, health, aedBank),
        ];
        foreach (var m in monthly)
        {
            var item = new RecurringExpense
            {
                Description = m.Name, Amount = m.Amount, Currency = m.Currency, CategoryId = m.Category.Id,
                AccountId = m.Account.Id, DayOfMonth = m.Day, StartDate = from,
            };
            var date = MonthlySchedule.FirstOnOrAfter(from, m.Day);
            for (; date <= today; date = MonthlySchedule.Next(date, m.Day))
            {
                var entry = NewExpense(m.Name, m.Amount, m.Currency, date, m.Category, m.Account);
                entry.RecurringExpense = item;
                db.Expenses.Add(entry);
            }
            item.NextDate = date;
            db.RecurringExpenses.Add(item);
        }

        // Everyday spending, generated the same way each time.
        var random = new Random(2026);
        decimal Between(int min, int max) => random.Next(min * 4, max * 4 + 1) / 4m;
        string Pick(params string[] options) => options[random.Next(options.Length)];

        for (var day = from; day <= today; day = day.AddDays(1))
        {
            if (day.DayNumber % 4 == 0)
                db.Expenses.Add(NewExpense(Pick("Carrefour groceries", "Spinneys", "Lulu Hypermarket", "Kibsons delivery"),
                    Between(120, 380), Currencies.AED, day, groceries, aedBank));
            if (day.DayOfWeek is DayOfWeek.Thursday or DayOfWeek.Saturday)
                db.Expenses.Add(NewExpense(Pick("Dinner at Bu Qtair", "Brunch at Tom & Serg", "Shake Shack", "Ravi Restaurant", "Sushi with friends"),
                    Between(60, 260), Currencies.AED, day, dining, aedBank));
            if (day.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Tuesday or DayOfWeek.Wednesday)
                db.Expenses.Add(NewExpense(Pick("Careem to office", "Dubai Metro top-up", "Careem home"),
                    Between(25, 90), Currencies.AED, day, transport, aedBank));
            if (day.Day is 8 or 20)
                db.Expenses.Add(NewExpense(Pick("Amazon.ae order", "Dubai Mall – clothes", "IKEA"),
                    Between(150, 600), Currencies.AED, day, shopping, aedBank));
            if (day.Day == 12)
                db.Expenses.Add(NewExpense(Pick("Amazon.ca gift for family", "Indigo books (online)"),
                    Between(45, 90), Currencies.CAD, day, shopping, cadCard));
            if (day.Day == 17)
                db.Expenses.Add(NewExpense("Cinema – VOX", Between(70, 110), Currencies.AED, day, fun, aedBank));
        }

        // A flight home booked in advance: dated next week, so it shows as pending.
        db.Expenses.Add(NewExpense("Flight DXB → YYZ (booked)", 1180m, Currencies.CAD, today.AddDays(7), travel, cadCard));

        await db.SaveChangesAsync(ct);
    }
}
