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

    /// <summary>The account or card it was paid with (optional).</summary>
    public int? AccountId { get; set; }
    public Account? Account { get; set; }

    /// <summary>Set when the expense was created by a recurring item.</summary>
    public int? RecurringExpenseId { get; set; }
    public RecurringExpense? RecurringExpense { get; set; }

    // Conversions to the home (CAD) and secondary (AED) currencies, fixed when the expense
    // is saved so history doesn't change when rates do. One of them is always the
    // original amount at a rate of 1.

    /// <summary>1 <see cref="Currency"/> = <see cref="FxRate"/> home currency.</summary>
    public decimal FxRate { get; set; } = 1m;

    /// <summary>The day the rates were published.</summary>
    public DateOnly RateDate { get; set; }

    public decimal AmountInHome { get; set; }

    /// <summary>1 <see cref="Currency"/> = <see cref="SecondaryFxRate"/> secondary currency.</summary>
    public decimal SecondaryFxRate { get; set; } = 1m;

    public decimal AmountInSecondary { get; set; }
}
