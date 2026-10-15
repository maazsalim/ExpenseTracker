using ExpenseTracker.Data;
using ExpenseTracker.Services;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Tests;

/// <summary>An in-memory database that can be opened as different users.</summary>
public class TestDb
{
    private readonly DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;

    /// <summary>A context for <paramref name="userId"/> (null = nobody signed in).</summary>
    public AppDbContext As(string? userId) => new(options, new FakeUser(userId));

    /// <summary>A context factory for <paramref name="userId"/>, as services receive it.</summary>
    public IDbContextFactory<AppDbContext> FactoryFor(string? userId) => new Factory(this, userId);

    private sealed record FakeUser(string? UserId) : ICurrentUser;

    private sealed class Factory(TestDb db, string? userId) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => db.As(userId);
    }
}
