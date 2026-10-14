using ExpenseTracker.Services;

namespace ExpenseTracker.Tests;

public class MonthlyScheduleTests
{
    [Theory]
    [InlineData("2026-10-01", 5, "2026-10-05")]   // later this month
    [InlineData("2026-10-05", 5, "2026-10-05")]   // today counts
    [InlineData("2026-10-06", 5, "2026-11-05")]   // already passed: next month
    [InlineData("2026-02-01", 31, "2026-02-28")]  // short month: last day
    [InlineData("2026-12-15", 1, "2027-01-01")]   // across the year end
    public void FirstOnOrAfter(string from, int day, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), MonthlySchedule.FirstOnOrAfter(DateOnly.Parse(from), day));
    }

    [Theory]
    [InlineData("2026-01-31", 31, "2026-02-28")]
    [InlineData("2026-02-28", 31, "2026-03-31")]  // back to the 31st after a short month
    [InlineData("2026-12-05", 5, "2027-01-05")]
    public void Next(string current, int day, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), MonthlySchedule.Next(DateOnly.Parse(current), day));
    }
}
