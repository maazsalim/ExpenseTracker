namespace ExpenseTracker.Services;

/// <summary>Date maths for "on day N of every month".</summary>
public static class MonthlySchedule
{
    /// <summary>Day <paramref name="day"/> of the month, or the month's last day if it's shorter.</summary>
    public static DateOnly OnDay(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    /// <summary>The first due date on or after <paramref name="from"/>.</summary>
    public static DateOnly FirstOnOrAfter(DateOnly from, int day)
    {
        var candidate = OnDay(from.Year, from.Month, day);
        return candidate >= from ? candidate : Next(candidate, day);
    }

    /// <summary>The due date in the month after <paramref name="current"/>.</summary>
    public static DateOnly Next(DateOnly current, int day)
    {
        var nextMonth = current.AddMonths(1);
        return OnDay(nextMonth.Year, nextMonth.Month, day);
    }
}
