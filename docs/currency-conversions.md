# How currency conversions work

**In one sentence:** every expense is saved in the currency you paid in, plus its amount in both CAD and AED at that day's rate, fixed when you save it. That way totals can be shown exactly in either currency, and old numbers never change when rates move.

## 1. Every rate goes through the US dollar

Free rate services based on the European Central Bank (ECB) don't include AED. But the UAE dirham is **pegged** to the US dollar at a fixed rate: **1 USD = 3.6725 AED**. So the app expresses every currency as "how many per 1 USD":

- **USD** is always 1.
- **AED** is always 3.6725. It's a fixed number in the code and is never looked up.
- **CAD** is fetched from [Frankfurter](https://frankfurter.dev), a free service with no API key that publishes the ECB's daily rates.

The rate between any two currencies is then a single division:

```
rate(from → to) = (to per USD) ÷ (from per USD)
```

> **Example:** you pay 900 AED. That day 1 USD = 1.4262 CAD.
> AED→CAD = 1.4262 ÷ 3.6725 = **0.38834**, so 900 AED × 0.38834 = **CAD 349.51**.

| File | What it does |
|---|---|
| [`Services/ExchangeRateService.cs`](../src/ExpenseTracker/Services/ExchangeRateService.cs) | `GetRateAsync` works out the rate between two currencies through USD. AED comes from the peg; other currencies come from the cache or Frankfurter. |
| [`Models/Currencies.cs`](../src/ExpenseTracker/Models/Currencies.cs) | Supported currencies and the `AedPerUsd = 3.6725` peg. |
| [`Services/FrankfurterRateSource.cs`](../src/ExpenseTracker/Services/FrankfurterRateSource.cs) | The only code that calls the internet. It asks Frankfurter for "1 USD in X on date D". |
| [`Services/IUsdRateSource.cs`](../src/ExpenseTracker/Services/IUsdRateSource.cs) | The interface behind the rate source, so tests can swap in a fake. |

## 2. Rates are cached and dated honestly

- Each fetched rate is saved in the **`ExchangeRates`** table, so a given day is only fetched once.
- **Weekends and holidays:** no rates are published on those days, so Frankfurter returns the last business day's rate. The app records both the day you asked for (`Date`) and the day the rate was actually published (`RateDate`). For example, an expense on a Saturday stores Friday as its rate date.
- **Future dates** use today's rate.
- **Today's rate isn't cached**, because it may not be published yet. It's fetched fresh each time.
- If the service can't be reached, you get a friendly "couldn't get the rate" message rather than a crash.

| File | What it does |
|---|---|
| [`Models/ExchangeRate.cs`](../src/ExpenseTracker/Models/ExchangeRate.cs) | One cached rate: `Date`, `RateDate`, `Base` (USD), `Quote` (e.g. CAD), `Rate`, `Source`. |

## 3. The conversion is fixed when you save

When you save an expense, the app stores these alongside the original amount:

| Field | Example | Meaning |
|---|---|---|
| `Amount` + `Currency` | 900.00 AED | what you actually paid |
| `FxRate` | 0.38834 | 1 AED = this many CAD |
| `AmountInHome` | 349.51 | the amount in CAD, rounded to cents |
| `SecondaryFxRate` | 1 | 1 AED = this many AED |
| `AmountInSecondary` | 900.00 | the amount in AED |
| `RateDate` | 13 Oct 2026 | the day the rate was published |

So every expense has its amount in **both** currencies. One of them is always the original (rate 1); the other is converted at that day's rate. A CAD expense works the other way round: `AmountInHome` is the original and `AmountInSecondary` is converted.

Because both amounts are stored, **past months never change** when exchange rates move. If you edit an expense, the rate is only fetched again if you change its **currency or date**. Changing just the amount reuses the stored rate.

Money is always stored as an exact decimal (`decimal` in C#, `numeric` in PostgreSQL), not a floating-point number, so no rounding errors creep in. Rates keep 8 decimal places. Converted amounts are rounded to cents, with halves rounding up (5.005 → 5.01).

| File | What it does |
|---|---|
| [`Models/Expense.cs`](../src/ExpenseTracker/Models/Expense.cs) | The fields above. |
| [`Services/ExpenseConverter.cs`](../src/ExpenseTracker/Services/ExpenseConverter.cs) | `ConvertAsync` fetches both rates and sets all the fields above. `ApplyRates` recalculates the amounts from the stored rates when only the amount changed. |
| [`Components/Pages/ExpenseForm.razor`](../src/ExpenseTracker/Components/Pages/ExpenseForm.razor) | `SaveAsync` calls the converter: `ConvertAsync` for a new expense or a changed currency/date, otherwise `ApplyRates`. |
| [`Services/Money.cs`](../src/ExpenseTracker/Services/Money.cs) | `Convert` (multiply and round to cents) and `Format` (e.g. "CAD 349.51"). |

## 4. Totals: the CAD | AED switch

- The **CAD | AED switch** at the top of the Expenses and Summary pages picks which stored amount every total adds up: `AmountInHome` for CAD, `AmountInSecondary` for AED. AED and CAD are never mixed in one sum, and **both views are exact**: an AED total counts AED purchases at exactly what you paid.
- The choice is remembered in your browser, and carries across pages.
- **Budgets** are set in CAD. In the AED view they're converted at **today's** rate and marked "≈", because a budget is a target, not a record. If today's rate can't be fetched, the AED button is disabled.
- The **Categories** page always shows CAD, because that's where budgets are set.
- In the expense list, each row shows its original amount, plus the converted amount underneath if it was paid in the other currency.

| File | What it does |
|---|---|
| [`Components/CurrencySwitch.razor`](../src/ExpenseTracker/Components/CurrencySwitch.razor) | The CAD \| AED buttons. Saves the choice in the browser's `localStorage` and restores it on load. |
| [`Services/DisplayCurrency.cs`](../src/ExpenseTracker/Services/DisplayCurrency.cs) | Remembers the current choice for the tab. `Of(expense)` gives the amount to add up, `Budget(cad)` converts a budget, and `Changed` tells pages to redraw. |
| [`Services/SecondaryCurrencyRate.cs`](../src/ExpenseTracker/Services/SecondaryCurrencyRate.cs) | Fetches today's CAD→AED rate once per visit, used for budgets in the AED view. |
| [`Services/CurrencyOptions.cs`](../src/ExpenseTracker/Services/CurrencyOptions.cs) | Settings: home currency (CAD), secondary currency (AED), and the default currency on the add-expense form (AED). |

> Changing the home or secondary currency later would mean converting every saved expense again, because both amounts are stored.

## 5. Caching

There are **two separate caches**, and they do different jobs.

### Cache 1: the `ExchangeRates` table (long-term memory)

Think of it as a **notebook of past rates kept in the database**. When the app needs "CAD per USD on 10 October":

1. **It checks the table first.** If that day is already there, it uses it, and no internet call is needed.
2. **If not, it asks Frankfurter**, then **saves the answer** for next time.

```
Need rate for 10 Oct → in the table? ── yes → use it
                                     └─ no  → ask Frankfurter → save to table → use it
```

- **It's permanent and shared.** It survives restarts, and every page and every user benefits from it.
- **Only past days are saved.** Today's rate might not be published yet, so saving it could keep a stale rate forever. Today is always fetched fresh.
- **Only CAD is stored.** AED never needs looking up, because it's fixed at 3.6725 per USD.

Where: `PerUsdAsync` in [`Services/ExchangeRateService.cs`](../src/ExpenseTracker/Services/ExchangeRateService.cs).

### Cache 2: once per circuit (short-term memory)

**What a circuit is:** Blazor Server works differently from a normal website. When you open the app in a browser tab, that tab keeps a **live connection** to the server, and the server keeps a little bundle of memory just for that tab. That bundle is the **circuit**: your tab's session on the server.

- Moving between Expenses, Summary and Categories → **same circuit**. The pages change, but the connection stays.
- Refreshing the page, opening a new tab, or reopening the app → **new circuit**.

**The cache:** showing budgets in the AED view needs **today's** CAD→AED rate, and every page and every budget on it would otherwise fetch it again. Instead, [`SecondaryCurrencyRate`](../src/ExpenseTracker/Services/SecondaryCurrencyRate.cs) does this:

```csharp
public Task<FxQuote?> GetAsync() => quote ??= LoadAsync();
```

In plain words: *"If I haven't fetched the rate yet, fetch it and remember it. If I already have, hand back the one I remembered."*

That memory belongs to the circuit, because the service is registered with `AddScoped` in [`Program.cs`](../src/ExpenseTracker/Program.cs). In Blazor Server, **"scoped" means one per circuit**. So:

- **One fetch per tab**, shared by every page you visit in that tab.
- **Refreshing gives a new circuit and a fresh fetch**, so the rate never gets more than one visit old.
- When you close the tab, the circuit and its remembered rate are thrown away.

The CAD | AED choice lives in the circuit the same way (in `DisplayCurrency`), which is why it carries across pages. It's also saved in the browser, so a new circuit picks it back up.

(Blazor first renders a quick preview of the page before the live connection starts, so opening the app can actually fetch twice. That's harmless.)

### Side by side

| | `ExchangeRates` table | Once per circuit |
|---|---|---|
| Stores | rates for **past days** | **today's** CAD→AED rate |
| Lives in | the database | the server's memory for your tab |
| Lasts | forever | until you refresh or close the tab |
| Shared by | everyone, always | the pages in one tab |
| Saves | calling Frankfurter again for a day already fetched | calling it on every page and budget |

**Not a cache:** the rates and amounts stored **on each expense** (`FxRate`, `AmountInHome`, `SecondaryFxRate`, `AmountInSecondary`). That's a **permanent record** of what was used when the expense was saved. It's never refreshed, which is the point: history doesn't change.

## The whole flow

```
You save "900 AED, Gym"
   → ExpenseConverter asks ExchangeRateService for AED → CAD and AED → AED on 13 Oct
       → AED per USD: 3.6725 (fixed)
       → CAD per USD: cache, or Frankfurter, then cache it → 1.4262
       → AED → CAD = 1.4262 ÷ 3.6725 = 0.38834;  AED → AED = 1
   → stored: 900 AED, CAD 349.51 (rate 0.38834), AED 900.00 (rate 1), rate date 13 Oct

Switch on CAD → pages add up AmountInHome      → "CAD 349.51"
Switch on AED → pages add up AmountInSecondary → "AED 900.00"
```

## Tests

[`ExchangeRateServiceTests.cs`](../tests/ExpenseTracker.Tests/ExchangeRateServiceTests.cs), [`ExpenseConverterTests.cs`](../tests/ExpenseTracker.Tests/ExpenseConverterTests.cs), [`DisplayCurrencyTests.cs`](../tests/ExpenseTracker.Tests/DisplayCurrencyTests.cs), [`MoneyTests.cs`](../tests/ExpenseTracker.Tests/MoneyTests.cs) and [`SecondaryCurrencyRateTests.cs`](../tests/ExpenseTracker.Tests/SecondaryCurrencyRateTests.cs) cover the AED peg, converting to both currencies, the switch, rounding, caching, weekend dates, future dates and an API outage. They use a fake rate source and an in-memory database, so they never touch the internet.

```bash
dotnet test
```

## Talking points

- **Money keeps its currency and the rate used.** Each amount is stored as it happened, along with the exact rate and date, so history can be checked and never changes.
- **AED is derived from the USD peg.** The free ECB-based rate source doesn't include AED, so it's calculated through the dollar, using the fact that the dirham's rate to the dollar is fixed.
- **Rate fetching is separate from the rest of the app.** Pages only ask an `IExchangeRateService` for a rate. Behind it, the rate source can be replaced (Frankfurter in the app, a fake in tests), and caching, weekends and failures are handled in one place.
- **It's tested** without the network, including what happens when the rate service is down.
