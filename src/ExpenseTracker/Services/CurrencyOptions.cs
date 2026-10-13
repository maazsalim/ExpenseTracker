using ExpenseTracker.Models;

namespace ExpenseTracker.Services;

public class CurrencyOptions
{
    public const string Section = "Currency";

    /// <summary>
    /// Every expense is converted into this when saved, and budgets are in it.
    /// Changing it later means re-converting existing expenses.
    /// </summary>
    public string HomeCurrency { get; set; } = Currencies.CAD;

    /// <summary>Totals are also shown, smaller, in this currency (converted at today's rate).</summary>
    public string SecondaryCurrency { get; set; } = Currencies.AED;

    /// <summary>Pre-selected currency on the add-expense form.</summary>
    public string DefaultExpenseCurrency { get; set; } = Currencies.AED;
}
