namespace ExpenseTracker.Services;

public static class Money
{
    /// <summary>Converts at <paramref name="rate"/> and rounds to cents (half away from zero).</summary>
    public static decimal Convert(decimal amount, decimal rate) =>
        Math.Round(amount * rate, 2, MidpointRounding.AwayFromZero);

    public static string Format(decimal amount, string currency) => $"{currency} {amount:N2}";
}
