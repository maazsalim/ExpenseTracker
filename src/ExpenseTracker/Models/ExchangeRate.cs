namespace ExpenseTracker.Models;

/// <summary>
/// A cached daily rate: 1 <see cref="Base"/> = <see cref="Rate"/> <see cref="Quote"/>.
/// <see cref="Date"/> is the day that was asked for; <see cref="RateDate"/> is the day the
/// rate was actually published (earlier on weekends and holidays).
/// </summary>
public class ExchangeRate
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public DateOnly RateDate { get; set; }
    public string Base { get; set; } = "";
    public string Quote { get; set; } = "";
    public decimal Rate { get; set; }
    public string Source { get; set; } = "";
    public DateTime FetchedAt { get; set; }
}
