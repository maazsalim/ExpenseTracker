using ExpenseTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<RecurringExpense> RecurringExpenses => Set<RecurringExpense>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Expense>().Property(e => e.Amount).HasPrecision(10, 2);
        b.Entity<Expense>().Property(e => e.Currency).HasMaxLength(3);
        b.Entity<Expense>().Property(e => e.FxRate).HasPrecision(18, 8);
        b.Entity<Expense>().Property(e => e.AmountInHome).HasPrecision(12, 2);
        b.Entity<Expense>().Property(e => e.SecondaryFxRate).HasPrecision(18, 8);
        b.Entity<Expense>().Property(e => e.AmountInSecondary).HasPrecision(12, 2);

        // Deleting a category must never silently delete its expenses.
        b.Entity<Expense>()
            .HasOne(e => e.Category)
            .WithMany(c => c.Expenses)
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Like categories, an account that has expenses can't be deleted.
        b.Entity<Expense>()
            .HasOne(e => e.Account)
            .WithMany(a => a.Expenses)
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Deleting a recurring item keeps the expenses it already created.
        b.Entity<Expense>()
            .HasOne(e => e.RecurringExpense)
            .WithMany()
            .HasForeignKey(e => e.RecurringExpenseId)
            .OnDelete(DeleteBehavior.SetNull);

        // One entry per recurring item per day, so two tabs opening at once can't both create it.
        b.Entity<Expense>().HasIndex(e => new { e.RecurringExpenseId, e.Date }).IsUnique();

        b.Entity<Account>(a =>
        {
            a.Property(x => x.Currency).HasMaxLength(3);
            a.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<RecurringExpense>(r =>
        {
            r.Property(x => x.Amount).HasPrecision(10, 2);
            r.Property(x => x.Currency).HasMaxLength(3);
            r.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            r.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Category>().Property(c => c.MonthlyBudget).HasPrecision(10, 2);
        b.Entity<Category>().Property(c => c.Color).HasDefaultValue("indigo");
        b.Entity<Category>().HasIndex(c => c.Name).IsUnique();

        b.Entity<ExchangeRate>(r =>
        {
            r.Property(x => x.Base).HasMaxLength(3);
            r.Property(x => x.Quote).HasMaxLength(3);
            r.Property(x => x.Source).HasMaxLength(50);
            r.Property(x => x.Rate).HasPrecision(18, 8);
            r.HasIndex(x => new { x.Date, x.Base, x.Quote }).IsUnique();
        });

        b.Entity<AppSettings>(s =>
        {
            s.Property(x => x.Id).ValueGeneratedNever();
            s.Property(x => x.TotalMonthlyBudget).HasPrecision(12, 2);
            s.HasData(new AppSettings { Id = AppSettings.SingletonId });
        });

        b.Entity<Category>().HasData(
            new Category { Id = 1, Name = "Food", Color = "indigo" },
            new Category { Id = 2, Name = "Transport", Color = "emerald" },
            new Category { Id = 3, Name = "Bills", Color = "orange" },
            new Category { Id = 4, Name = "Entertainment", Color = "pink" },
            new Category { Id = 5, Name = "Other", Color = "sky" });
    }
}
