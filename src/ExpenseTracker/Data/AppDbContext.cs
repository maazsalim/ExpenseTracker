using ExpenseTracker.Models;
using ExpenseTracker.Services;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Data;

/// <summary>
/// The app's database. User-owned data (<see cref="IUserOwned"/>) is filtered to the signed-in
/// user on every query and stamped with their ID when added, so pages can't leak or mix up data.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser? currentUser = null)
    : IdentityDbContext<IdentityUser>(options), IDataProtectionKeyContext
{
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<RecurringExpense> RecurringExpenses => Set<RecurringExpense>();

    /// <summary>Keys that protect sign-in cookies, kept here so restarts don't sign users out.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    // Read per query, so each context (and its filters) follows the user it was created for.
    private string? CurrentUserId => currentUser?.UserId;

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b); // Identity tables

        ConfigureUserOwned<Expense>(b);
        ConfigureUserOwned<Category>(b);
        ConfigureUserOwned<Account>(b);
        ConfigureUserOwned<RecurringExpense>(b);
        ConfigureUserOwned<AppSettings>(b);

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
            a.HasIndex(x => new { x.UserId, x.Name }).IsUnique();
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
        b.Entity<Category>().HasIndex(c => new { c.UserId, c.Name }).IsUnique();

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
            s.Property(x => x.TotalMonthlyBudget).HasPrecision(12, 2);
            s.HasIndex(x => x.UserId).IsUnique();
        });

        b.Entity<Category>().HasData(
            new Category { Id = 1, Name = "Food", Color = "indigo" },
            new Category { Id = 2, Name = "Transport", Color = "emerald" },
            new Category { Id = 3, Name = "Bills", Color = "orange" },
            new Category { Id = 4, Name = "Entertainment", Color = "pink" },
            new Category { Id = 5, Name = "Other", Color = "sky" });
    }

    private void ConfigureUserOwned<T>(ModelBuilder b) where T : class, IUserOwned
    {
        // No signed-in user means no rows at all.
        b.Entity<T>().HasQueryFilter(x => CurrentUserId != null && x.UserId == CurrentUserId);
        b.Entity<T>()
            .HasOne<IdentityUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampOwner();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampOwner();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// New rows belong to the signed-in user; changing someone else's row is refused.
    /// </summary>
    private void StampOwner()
    {
        foreach (var entry in ChangeTracker.Entries<IUserOwned>())
        {
            switch (entry.State)
            {
                case EntityState.Added when string.IsNullOrEmpty(entry.Entity.UserId):
                    entry.Entity.UserId = CurrentUserId
                        ?? throw new InvalidOperationException("Can't save data without a signed-in user.");
                    break;

                case EntityState.Added or EntityState.Modified or EntityState.Deleted
                    when entry.Entity.UserId != CurrentUserId:
                    throw new InvalidOperationException("Can't change data that belongs to another user.");
            }
        }
    }
}
