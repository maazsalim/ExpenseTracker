using ExpenseTracker.Data;
using ExpenseTracker.Models;
using ExpenseTracker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace ExpenseTracker.Tests;

public class ExchangeRateServiceTests
{
    // Tuesday 13 Oct 2026. Friday 9 Oct is the last business day before the weekend of 10–11 Oct.
    private static readonly DateOnly Today = new(2026, 10, 13);
    private static readonly DateOnly Friday = new(2026, 10, 9);
    private static readonly DateOnly Saturday = new(2026, 10, 10);

    private const decimal CadPerUsd = 1.4226m;

    private readonly FakeRateSource source = new();
    private readonly TestDbFactory dbFactory = new();
    private readonly ExchangeRateService service;

    public ExchangeRateServiceTests()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero));
        service = new ExchangeRateService(dbFactory, source, clock);
        source.Rates[(Currencies.CAD, Friday)] = new UsdRate(CadPerUsd, Friday);
        // Like Frankfurter: a weekend day returns Friday's rate.
        source.Rates[(Currencies.CAD, Saturday)] = new UsdRate(CadPerUsd, Friday);
        source.Rates[(Currencies.CAD, Today)] = new UsdRate(1.4300m, Today);
    }

    [Fact]
    public async Task SameCurrency_IsOne_WithoutCallingTheApi()
    {
        var quote = await service.GetRateAsync("CAD", "CAD", Friday);

        Assert.Equal(1m, quote.Rate);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task UsdToCad_UsesTheSourceRate()
    {
        var quote = await service.GetRateAsync("USD", "CAD", Friday);

        Assert.Equal(CadPerUsd, quote.Rate);
        Assert.Equal(Friday, quote.RateDate);
    }

    [Fact]
    public async Task AedToCad_IsDerivedFromTheUsdPeg()
    {
        var quote = await service.GetRateAsync("AED", "CAD", Friday);

        Assert.Equal(Math.Round(CadPerUsd / 3.6725m, 8), quote.Rate);
        Assert.Equal(0.38736555m, quote.Rate);
        Assert.Equal("peg+frankfurter-fake", quote.Source);
        Assert.DoesNotContain(source.Requested, r => r.Currency == Currencies.AED);
    }

    [Fact]
    public async Task CadToAed_IsTheInverse()
    {
        var quote = await service.GetRateAsync("CAD", "AED", Friday);

        Assert.Equal(Math.Round(3.6725m / CadPerUsd, 8), quote.Rate);
    }

    [Fact]
    public async Task AedAmount_ConvertsToCad()
    {
        var quote = await service.GetRateAsync("AED", "CAD", Friday);

        Assert.Equal(38.74m, Money.Convert(100m, quote.Rate));
    }

    [Fact]
    public async Task PastDay_IsCached_SoTheApiIsCalledOnce()
    {
        await service.GetRateAsync("AED", "CAD", Friday);
        await service.GetRateAsync("AED", "CAD", Friday);

        Assert.Equal(1, source.Calls);
        using var db = dbFactory.CreateDbContext();
        var row = Assert.Single(db.ExchangeRates);
        Assert.Equal((Friday, "USD", "CAD", CadPerUsd), (row.Date, row.Base, row.Quote, row.Rate));
    }

    [Fact]
    public async Task Weekend_UsesTheEarlierRate_AndKeepsItsRealDate()
    {
        var quote = await service.GetRateAsync("USD", "CAD", Saturday);

        Assert.Equal(CadPerUsd, quote.Rate);
        Assert.Equal(Friday, quote.RateDate);

        using var db = dbFactory.CreateDbContext();
        var row = Assert.Single(db.ExchangeRates);
        Assert.Equal((Saturday, Friday), (row.Date, row.RateDate));
    }

    [Fact]
    public async Task Today_IsNotCached_BecauseItMayNotBePublishedYet()
    {
        await service.GetRateAsync("USD", "CAD", Today);
        await service.GetRateAsync("USD", "CAD", Today);

        Assert.Equal(2, source.Calls);
        using var db = dbFactory.CreateDbContext();
        Assert.Empty(db.ExchangeRates);
    }

    [Fact]
    public async Task FutureDate_UsesTodaysRate()
    {
        var quote = await service.GetRateAsync("USD", "CAD", Today.AddDays(5));

        Assert.Equal(1.4300m, quote.Rate);
        Assert.Equal(Today, source.Requested.Single().Date);
    }

    [Fact]
    public async Task ApiFailure_GivesAFriendlyError()
    {
        source.Fail = true;

        await Assert.ThrowsAsync<ExchangeRateUnavailableException>(
            () => service.GetRateAsync("AED", "CAD", Friday));
    }

    [Fact]
    public async Task ApiFailure_FallsBackToARecentSavedRate_WithItsRealDate()
    {
        await service.GetRateAsync("USD", "CAD", Friday); // saves Friday's rate
        source.Fail = true;

        var quote = await service.GetRateAsync("USD", "CAD", Today);

        Assert.Equal(CadPerUsd, quote.Rate);
        Assert.Equal(Friday, quote.RateDate);
    }

    [Fact]
    public async Task LatestSavedRate_UsesTheNewestSavedRate_WithoutCallingTheApi()
    {
        Assert.Null(await service.GetLatestSavedRateAsync("AED", "CAD")); // nothing saved yet

        await service.GetRateAsync("USD", "CAD", Friday); // saves Friday's rate
        var callsBefore = source.Calls;

        var quote = await service.GetLatestSavedRateAsync("AED", "CAD");

        Assert.Equal(Math.Round(CadPerUsd / 3.6725m, 8), quote!.Rate);
        Assert.Equal(Friday, quote.RateDate);
        Assert.Equal(callsBefore, source.Calls);
    }

    [Fact]
    public async Task ApiFailure_DoesntUseASavedRateOlderThanAWeek()
    {
        var eightDaysEarlier = Friday.AddDays(-8);
        source.Rates[(Currencies.CAD, eightDaysEarlier)] = new UsdRate(1.40m, eightDaysEarlier);
        await service.GetRateAsync("USD", "CAD", eightDaysEarlier); // saves that day's rate
        source.Fail = true;

        await Assert.ThrowsAsync<ExchangeRateUnavailableException>(
            () => service.GetRateAsync("USD", "CAD", Friday));
    }

    private class FakeRateSource : IUsdRateSource
    {
        public Dictionary<(string, DateOnly), UsdRate> Rates { get; } = new();
        public List<(string Currency, DateOnly Date)> Requested { get; } = new();
        public int Calls => Requested.Count;
        public bool Fail { get; set; }

        public string Name => "frankfurter-fake";

        public Task<UsdRate> GetAsync(string currency, DateOnly date, CancellationToken ct = default)
        {
            Requested.Add((currency, date));
            if (Fail)
                throw new HttpRequestException("offline");
            return Task.FromResult(Rates[(currency, date)]);
        }
    }

    private class TestDbFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        public AppDbContext CreateDbContext() => new(options);
    }
}
