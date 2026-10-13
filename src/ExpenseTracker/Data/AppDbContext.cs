using ExpenseTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Expense>().Property(e => e.Amount).HasPrecision(10, 2);

        // Deleting a category must never silently delete its expenses.
        b.Entity<Expense>()
            .HasOne(e => e.Category)
            .WithMany(c => c.Expenses)
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<Category>().Property(c => c.MonthlyBudget).HasPrecision(10, 2);
        b.Entity<Category>().Property(c => c.Color).HasDefaultValue("indigo");
        b.Entity<Category>().HasIndex(c => c.Name).IsUnique();

        b.Entity<Category>().HasData(
            new Category { Id = 1, Name = "Food", Color = "indigo" },
            new Category { Id = 2, Name = "Transport", Color = "emerald" },
            new Category { Id = 3, Name = "Bills", Color = "orange" },
            new Category { Id = 4, Name = "Entertainment", Color = "pink" },
            new Category { Id = 5, Name = "Other", Color = "sky" });
    }
}
