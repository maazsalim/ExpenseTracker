using ExpenseTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Expense>().Property(e => e.Amount).HasPrecision(10, 2);
        b.Entity<Expense>().Property(e => e.Currency).HasMaxLength(3);
        b.Entity<Expense>().Property(e => e.FxRate).HasPrecision(18, 8);
        b.Entity<Expense>().Property(e => e.AmountInHome).HasPrecision(12, 2);

        // Deleting a category must never silently delete its expenses.
        b.Entity<Expense>()
            .HasOne(e => e.Category)
            .WithMany(c => c.Expenses)
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

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
