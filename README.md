# Dual Expense Tracker

Track spending across two currencies, **UAE dirhams (AED)** and **Canadian dollars (CAD)**, with totals that are exact in either one. Built for life split between Dubai and Canada, and installable on an iPhone.

## Try it

**Live app:** https://dual-expense-maaz-cngvdrcrahfjfker.francecentral-01.azurewebsites.net

| | |
|---|---|
| **Email** | `demo@example.com` |
| **Password** | `Try-Dual-Expense-2026` |

The demo account has made-up data (rent and groceries in Dubai, subscriptions on a Canadian card, a flight home booked in advance) that **resets every day**, so feel free to add, edit or delete anything.

> It runs on free hosting that sleeps when idle, so the **first load can take 10–20 seconds**. After that it's quick.

**On a phone:** open the link in Safari → Share → **Add to Home Screen** to install it as an app.

## What it does

- **Two currencies, exact totals.** Every expense keeps the currency it was paid in, plus its amount in both CAD and AED at that day's rate. A **CAD | AED switch** shows every total in either currency.
- **The dirham peg.** Free exchange-rate sources don't include AED, so it's derived from the US dollar using the fixed peg (1 USD = 3.6725 AED).
- **Budgets:** per category and overall, with progress bars.
- **Recurring expenses** (rent, subscriptions) added automatically each month, catching up on any months missed.
- **Pending amounts:** recurring items still due this month and expenses dated after today show as a separate "pending" figure, so you can see where the month will land.
- **"Paid with"**: which account or card paid, with totals per account and per currency.
- **Sign-in**, with every user's data kept separate.
- **Installable on iPhone** (PWA): full-screen, home-screen icon, and it reconnects quietly after being in the background.

## How it's built

| | |
|---|---|
| **App** | ASP.NET Core **Blazor Server** (.NET 10), C# |
| **Data** | **PostgreSQL** with Entity Framework Core and migrations |
| **Sign-in** | ASP.NET Core Identity (cookies) |
| **Exchange rates** | [Frankfurter](https://frankfurter.dev) (ECB rates), cached in the database |
| **Hosting** | Azure App Service (Free tier, Linux) + [Neon](https://neon.tech) serverless Postgres (free tier) |
| **CI/CD** | GitHub Actions: build → test → apply database migrations → deploy, on every push to `main` |
| **Tests** | xUnit: currency conversion and rounding, the peg, caching, recurring schedules, pending amounts, and per-user data isolation |

### Design notes

- **Money is never a floating-point number**: `decimal` in C#, `numeric` in Postgres. Conversions are fixed when an expense is saved, so history never changes when rates move.
- **Per-user data isolation is enforced centrally**: an EF Core global query filter limits every query to the signed-in user (and returns nothing if nobody is signed in), and saving refuses changes to another user's data. Tests with two users prove it.
- **No background timers**: the free host sleeps, so recurring entries and the demo reset happen when the app is opened.
- **Resilient to the rate service being slow or down**: pages never wait for it; today's rate is shared app-wide, and saved rates are used as a fallback.

More detail in [`docs/`](docs):
[currency conversions](docs/currency-conversions.md) ·
[recurring expenses](docs/recurring-expenses.md) ·
[sign-in and per-user data](docs/sign-in.md)

## Running it locally

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) and PostgreSQL (e.g. [Postgres.app](https://postgresapp.com) on a Mac).

```bash
git clone https://github.com/maazsalim/ExpenseTracker.git
cd ExpenseTracker
dotnet tool install --global dotnet-ef                   # once, for database migrations
dotnet ef database update --project src/ExpenseTracker   # creates the tables
dotnet run --project src/ExpenseTracker                  # http://localhost:5117
```

Set your PostgreSQL username in the connection string in `src/ExpenseTracker/appsettings.json` first. Then sign in with the demo account above; sample data loads on first sign-in.

```bash
dotnet test   # run the tests
```
