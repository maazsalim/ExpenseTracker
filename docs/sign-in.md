# How sign-in and per-user data work

**In one sentence:** you sign in with ASP.NET Core Identity, every page requires it, and the database layer automatically limits every query to the signed-in user's data, so pages can't show or change anyone else's.

## Signing in

| Piece | What it does | File |
|---|---|---|
| **Login page** | Email + password form, with "Keep me signed in". Rendered as a plain form post (no live connection), because signing in has to set a cookie on a normal web response. After signing in, it returns to the page you asked for, but only ever to a page in this app. | [`Components/Account/Login.razor`](../src/ExpenseTracker/Components/Account/Login.razor) |
| **Every page locked** | `@attribute [Authorize]` applies to every page in `Pages/`. `Error` and `NotFound` opt out with `[AllowAnonymous]`. | [`Components/Pages/_Imports.razor`](../src/ExpenseTracker/Components/Pages/_Imports.razor) |
| **Redirect to login** | Visiting a page while signed out sends you to `/login?ReturnUrl=…`. | [`Routes.razor`](../src/ExpenseTracker/Components/Routes.razor), [`Account/RedirectToLogin.razor`](../src/ExpenseTracker/Components/Account/RedirectToLogin.razor) |
| **Sign out** | A button in the menu posts to `/logout` (protected against cross-site requests by the antiforgery check). | [`Layout/NavMenu.razor`](../src/ExpenseTracker/Components/Layout/NavMenu.razor), [`Program.cs`](../src/ExpenseTracker/Program.cs) |
| **Setup** | Identity with cookies, 30-day "keep me signed in" renewed while you use the app, and lockout for 5 minutes after 5 wrong passwords. | [`Program.cs`](../src/ExpenseTracker/Program.cs) |

### No sign-up: the owner comes from configuration

There's no registration page. At startup, [`OwnerAccount`](../src/ExpenseTracker/Services/OwnerAccount.cs) reads `Owner:Email` and `Owner:Password`:

- If that account doesn't exist yet, it's **created**.
- Any rows **without an owner** are given to it. These are data from before sign-in existed, and starter data added by migrations (the first categories and accounts).
- If the settings are missing, a warning is logged and nobody can sign in.

**Where the settings live:**
- **Locally:** in [`appsettings.Development.json`](../src/ExpenseTracker/appsettings.Development.json). Git stores this file with **blank** values, and it's marked `skip-worktree`, so git ignores the real values on your machine and they're never committed. After cloning the repo somewhere new, run this once before filling it in:

  ```bash
  git update-index --skip-worktree src/ExpenseTracker/appsettings.Development.json
  ```

- **Hosted (Phase 5):** in Azure's app settings.

Once the account exists, changing the password in settings doesn't change it. The password is only used to create the account.

### Staying signed in on the free host

Sign-in cookies are protected by secret keys. If the keys only lived in memory, every restart (the free host sleeps and wakes up) would sign you out. They're stored in the `DataProtectionKeys` table instead (`PersistKeysToDbContext` in `Program.cs`), so restarts don't matter.

## Per-user data

**The idea:** pages never filter by user themselves. The database layer does it for every query, so a page *can't* forget and show someone else's data.

### 1. Knowing who's signed in

[`CurrentUser.cs`](../src/ExpenseTracker/Services/CurrentUser.cs) answers "what's the signed-in user's ID?":

- **On the first page load** (and the login page), it reads the user from the web request.
- **Once the page is live**, there's no request any more, so `UserCircuitHandler` stores the user in the circuit when the live connection starts. (See [currency-conversions.md](currency-conversions.md#5-caching) for what a circuit is.)

`AppDbContext` receives this through its constructor (`ICurrentUser`). The context factory is registered as **scoped** in `Program.cs`, so each request or browser tab gets contexts tied to its own user.

### 2. Every read is filtered

Each kind of user-owned data (`Expense`, `Category`, `Account`, `RecurringExpense`, `AppSettings`) implements [`IUserOwned`](../src/ExpenseTracker/Models/IUserOwned.cs), which just means it has a `UserId`. In [`AppDbContext.cs`](../src/ExpenseTracker/Data/AppDbContext.cs), `ConfigureUserOwned<T>` adds an EF Core **global query filter** to each of them:

```csharp
b.Entity<T>().HasQueryFilter(x => CurrentUserId != null && x.UserId == CurrentUserId);
```

EF adds this to **every** query on that table. A page writes:

```csharp
db.Expenses.Where(e => e.Date >= start).ToListAsync()
```

and the SQL that actually runs is:

```sql
SELECT ... FROM "Expenses" WHERE "UserId" = @currentUser AND "Date" >= @start
```

It also applies to:

- **Related data**, such as `category.Expenses.Count`: only your expenses are counted.
- **Looking up by ID** (`Find(id)`): another user's ID simply returns "not found".
- **Bulk updates and deletes** (`ExecuteUpdate`, `ExecuteDelete`): these can only touch your own rows.

The `CurrentUserId != null` part means that **if nobody is signed in, every query returns nothing**. It *fails closed*: if something goes wrong, the result is that nothing is shown, never that everything is.

### 3. Every write is checked

`SaveChanges` is overridden to run `StampOwner()` first:

- **New rows** automatically get the signed-in user's ID. Pages never set `UserId` themselves.
- **Adding, changing or deleting a row that belongs to someone else** throws an error, and nothing is saved.
- **Saving with nobody signed in** throws an error.

### 4. The one deliberate exception

`IgnoreQueryFilters()` switches the filter off. It's only used in `OwnerAccount`, at startup, to find rows with no owner. Pages never use it.

### What isn't filtered

- **`ExchangeRates`** is shared on purpose, because a rate is the same for everyone.
- **Raw SQL** (`ExecuteSqlRaw`) skips the filter. The app only uses it in migrations.

### Other per-user details

- Names only need to be unique **per user**: the unique indexes are on (`UserId`, `Name`) for categories and accounts.
- Settings are **one row per user**, created the first time they're saved, so pages read them with `SingleOrDefaultAsync()`.
- Deleting a user is blocked while they still have data (`Restrict` foreign keys).

## The flow

```
Request a page while signed out
   → [Authorize] → redirect to /login?ReturnUrl=/summary
   → POST email + password → Identity checks it (lockout after 5 tries)
   → cookie set (protected by keys stored in the database) → back to /summary

On the page
   → CurrentUser = user from the request, then from the circuit
   → every query:  ... WHERE "UserId" = <you>
   → every save:   new rows get UserId = <you>; other users' rows are refused
```

## Tests

[`UserIsolationTests.cs`](../tests/ExpenseTracker.Tests/UserIsolationTests.cs) creates two users, Alice and Bob, with the same kinds of data, and checks that:

- each only sees their own data, including through related data;
- Alice can't find, change or delete Bob's rows;
- new rows belong to whoever added them;
- with nobody signed in, nothing is visible and nothing can be saved;
- running recurring items as Alice never touches Bob's.

They use an in-memory database opened "as" a given user ([`TestDb.cs`](../tests/ExpenseTracker.Tests/TestDb.cs)).

## Adding another person later

The data model already supports it. What's missing is a way to create their account (an admin page, or another entry in configuration). They'd start with no categories or accounts, because the starter ones were given to the owner.

## Talking points

- **Standard, well-tested sign-in**: ASP.NET Core Identity, with no custom password handling. There's no public sign-up; the owner account comes from configuration.
- **Data isolation is enforced centrally in the DbContext**, not in each page. A global query filter scopes every query to the signed-in user and **fails closed** when nobody is signed in.
- **Writes are checked too**: `SaveChanges` stamps the owner on new rows and refuses changes to another user's rows.
- **Blazor Server detail**: the signed-in user is read from the request at first, then from the circuit via a `CircuitHandler`, so services know who's signed in once the page is live.
- **It survives the free host sleeping**: cookie keys are stored in the database.
- **Proven by tests** with two users.
