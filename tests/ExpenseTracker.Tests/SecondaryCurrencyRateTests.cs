using ExpenseTracker.Models;
using ExpenseTracker.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExpenseTracker.Tests;

public class SecondaryCurrencyRateTests
{
    private readonly FakeFx fx = new();

    // Shared by every instance, like the app-wide cache in the real app.
    private readonly MemoryCache cache = new(new MemoryCacheOptions());

    private SecondaryCurrencyRate Create(string secondary = Currencies.AED) => new(
        fx,
        Options.Create(new CurrencyOptions { HomeCurrency = Currencies.CAD, SecondaryCurrency = secondary }),
        cache,
        TimeProvider.System,
        NullLogger<SecondaryCurrencyRate>.Instance);

    [Fact]
    public async Task OtherTabs_ReuseTheSameRate()
    {
        await Create().GetAsync();
        var otherTab = await Create().GetAsync();

        Assert.Equal(2.5815m, otherTab!.Rate);
        Assert.Equal(1, fx.Calls);
    }

    [Fact]
    public async Task AFailure_IsNotRetriedStraightAway()
    {
        fx.Fail = true;
        await Create().GetAsync();

        fx.Fail = false;
        Assert.Null(await Create().GetAsync()); // still within the 5-minute wait
        Assert.Equal(1, fx.Calls);
    }

    [Fact]
    public async Task FetchesTheRateOnce_AndReusesIt()
    {
        var rates = Create();

        var first = await rates.GetAsync();
        var second = await rates.GetAsync();

        Assert.Equal(2.5815m, first!.Rate);
        Assert.Same(first, second);
        Assert.Equal(1, fx.Calls);
        Assert.Equal((Currencies.CAD, Currencies.AED), fx.LastPair);
    }

    [Fact]
    public async Task SameAsHome_ShowsNothing()
    {
        Assert.Null(await Create(Currencies.CAD).GetAsync());
        Assert.Equal(0, fx.Calls);
    }

    [Fact]
    public async Task RateUnavailable_ShowsNothing()
    {
        fx.Fail = true;

        Assert.Null(await Create().GetAsync());
    }

    private class FakeFx : IExchangeRateService
    {
        public int Calls { get; private set; }
        public (string, string) LastPair { get; private set; }
        public bool Fail { get; set; }

        public Task<FxQuote> GetRateAsync(string from, string to, DateOnly date, CancellationToken ct = default)
        {
            Calls++;
            LastPair = (from, to);
            if (Fail)
                throw new ExchangeRateUnavailableException("offline");
            return Task.FromResult(new FxQuote(2.5815m, date, "test"));
        }
    }
}
