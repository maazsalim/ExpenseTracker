namespace ExpenseTracker.Models;

/// <summary>ISO 4217 codes the app supports.</summary>
public static class Currencies
{
    public const string AED = "AED";
    public const string CAD = "CAD";
    public const string USD = "USD";

    /// <summary>Currencies an expense can be entered in.</summary>
    public static readonly string[] Supported = [AED, CAD];

    /// <summary>The UAE dirham is pegged: 1 USD = 3.6725 AED.</summary>
    public const decimal AedPerUsd = 3.6725m;

    public static bool IsSupported(string? code) => code is not null && Supported.Contains(code);
}
