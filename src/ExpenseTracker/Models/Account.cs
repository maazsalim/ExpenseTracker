using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Models;

/// <summary>A bank account or card an expense was paid with.</summary>
public class Account : IUserOwned
{
    public int Id { get; set; }

    public string? UserId { get; set; }

    [Required, StringLength(50)]
    public string Name { get; set; } = "";

    /// <summary>The currency the account is held in (a CAD card can still pay in AED).</summary>
    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = Currencies.AED;

    public List<Expense> Expenses { get; set; } = new();
}
