using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Models;

public class Category
{
    public static readonly string[] Colors =
        ["indigo", "emerald", "orange", "pink", "sky", "amber", "violet", "teal", "rose", "slate"];

    public int Id { get; set; }

    [Required, StringLength(50)]
    public string Name { get; set; } = "";

    [Required, StringLength(20)]
    public string Color { get; set; } = "indigo";

    [Range(0.01, 1_000_000, ErrorMessage = "Budget must be greater than 0")]
    public decimal? MonthlyBudget { get; set; }

    public List<Expense> Expenses { get; set; } = new();
}
