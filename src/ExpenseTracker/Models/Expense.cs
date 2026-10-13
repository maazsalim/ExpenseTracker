using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Models;

public class Expense
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Description { get; set; } = "";

    /// <summary>The amount in the currency it was paid in.</summary>
    [Range(0.01, 1_000_000)]
    public decimal Amount { get; set; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = Currencies.AED;

    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Range(1, int.MaxValue, ErrorMessage = "Pick a category")]
    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    // Conversion to the home currency, fixed when the expense is saved so history
    // doesn't change when rates do.

    /// <summary>1 <see cref="Currency"/> = <see cref="FxRate"/> home currency.</summary>
    public decimal FxRate { get; set; } = 1m;

    /// <summary>The day <see cref="FxRate"/> was published.</summary>
    public DateOnly RateDate { get; set; }

    public decimal AmountInHome { get; set; }
}
