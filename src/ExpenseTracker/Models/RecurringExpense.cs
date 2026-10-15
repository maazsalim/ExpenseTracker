using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Models;

/// <summary>
/// An expense that repeats monthly (rent, subscriptions). Due entries are created when the
/// app is opened, catching up on any missed months.
/// </summary>
public class RecurringExpense : IUserOwned
{
    public int Id { get; set; }

    public string? UserId { get; set; }

    [Required, StringLength(100)]
    public string Description { get; set; } = "";

    [Range(0.01, 1_000_000)]
    public decimal Amount { get; set; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = Currencies.AED;

    [Range(1, int.MaxValue, ErrorMessage = "Pick a category")]
    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    public int? AccountId { get; set; }
    public Account? Account { get; set; }

    /// <summary>Day of the month it's due. In shorter months, days past the end fall on the last day.</summary>
    [Range(1, 31, ErrorMessage = "Pick a day from 1 to 31")]
    public int DayOfMonth { get; set; } = 1;

    /// <summary>The first month it can be due in. A start date in the past creates the missed entries.</summary>
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    /// <summary>When the next entry should be created.</summary>
    public DateOnly NextDate { get; set; }

    /// <summary>Paused items create nothing.</summary>
    public bool Active { get; set; } = true;
}
