using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Models;

/// <summary>A user's settings: at most one row per user, created when first saved.</summary>
public class AppSettings : IUserOwned
{
    public int Id { get; set; }

    public string? UserId { get; set; }

    /// <summary>A budget for all spending in a month, in the home currency, regardless of category.</summary>
    [Range(0.01, 10_000_000, ErrorMessage = "Budget must be greater than 0")]
    public decimal? TotalMonthlyBudget { get; set; }

    /// <summary>
    /// When sample data was last loaded (UTC). Only set for the demo account, whose data is
    /// reloaded once a day.
    /// </summary>
    public DateTime? SampleDataLoadedAt { get; set; }
}
