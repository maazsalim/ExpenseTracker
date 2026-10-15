namespace ExpenseTracker.Models;

/// <summary>
/// Data that belongs to one user. <see cref="Data.AppDbContext"/> only ever returns the signed-in
/// user's rows and fills in <see cref="UserId"/> when they're added.
/// </summary>
public interface IUserOwned
{
    /// <summary>
    /// The owner's ID. Null only for rows created by migrations (seed data) before the owner
    /// account existed; those are given to the owner at startup.
    /// </summary>
    string? UserId { get; set; }
}
