using System.ComponentModel.DataAnnotations;
using ExpenseTracker.Models;

namespace ExpenseTracker.Tests;

public class ExpenseValidationTests
{
    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static Expense ValidExpense() =>
        new() { Description = "Groceries", Amount = 42.50m, CategoryId = 1 };

    [Fact]
    public void ValidExpense_HasNoErrors()
    {
        Assert.Empty(Validate(ValidExpense()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Amount_MustBePositive(decimal amount)
    {
        var expense = ValidExpense();
        expense.Amount = amount;

        Assert.Contains(Validate(expense), r => r.MemberNames.Contains(nameof(Expense.Amount)));
    }

    [Fact]
    public void Description_IsRequired()
    {
        var expense = ValidExpense();
        expense.Description = "";

        Assert.Contains(Validate(expense), r => r.MemberNames.Contains(nameof(Expense.Description)));
    }

    [Fact]
    public void Category_MustBePicked()
    {
        var expense = ValidExpense();
        expense.CategoryId = 0;

        Assert.Contains(Validate(expense), r => r.MemberNames.Contains(nameof(Expense.CategoryId)));
    }
}
