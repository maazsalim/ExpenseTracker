using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Models;

/// <summary>App-wide settings, stored as a single row (<see cref="SingletonId"/>).</summary>
public class AppSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>A budget for all spending in a month, in the home currency, regardless of category.</summary>
    [Range(0.01, 10_000_000, ErrorMessage = "Budget must be greater than 0")]
    public decimal? TotalMonthlyBudget { get; set; }
}
