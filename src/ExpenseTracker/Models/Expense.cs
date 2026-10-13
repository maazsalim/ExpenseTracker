using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Models;

public class Expense
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Description { get; set; } = "";

    [Range(0.01, 1_000_000)]
    public decimal Amount { get; set; }

    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Range(1, int.MaxValue, ErrorMessage = "Pick a category")]
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
}