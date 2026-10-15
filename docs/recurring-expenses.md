# How recurring expenses work

**In one sentence:** a recurring item is a template with a "next due" date. Whenever the app is opened, every item whose date has arrived creates its expense(s) and moves its date on by a month, catching up on any months that were missed.

## The pieces

| Piece | What it is | File |
|---|---|---|
| **The data** | A `RecurringExpense` is a template: description, amount, currency, category, account, **day of month**, **`NextDate`** (when the next entry is due) and `Active` (paused or not). Each expense it creates is linked back to it with `Expense.RecurringExpenseId`. | [`Models/RecurringExpense.cs`](../src/ExpenseTracker/Models/RecurringExpense.cs) |
| **The date maths** | `MonthlySchedule`: small, pure functions such as "first due date on or after X" and "next due date after X". It handles short months, so the 31st becomes the 28th in February and goes back to the 31st in March. | [`Services/MonthlySchedule.cs`](../src/ExpenseTracker/Services/MonthlySchedule.cs) |
| **The engine** | `RecurringExpenseService.CreateDueAsync`: finds active items whose `NextDate` has arrived, creates an expense for each due date (converted to CAD and AED like any expense), and moves `NextDate` on by a month. | [`Services/RecurringExpenseService.cs`](../src/ExpenseTracker/Services/RecurringExpenseService.cs) |
| **The screens** | The Recurring page to add, edit, pause and delete items, and the "Repeat every month" checkbox on the expense form. | [`Pages/Recurring.razor`](../src/ExpenseTracker/Components/Pages/Recurring.razor), [`Pages/ExpenseForm.razor`](../src/ExpenseTracker/Components/Pages/ExpenseForm.razor) |

## Design decisions

### 1. No timer, because the free host sleeps

A background job that runs "on the 1st at midnight" won't fire if the server is asleep. So the work is done **when the app is opened**: the Expenses and Summary pages call `RunOnceAsync()`.

- It **catches up**: if the app hasn't been opened for three months, all three missed entries are created.
- It runs **once per circuit** (per browser tab), so moving between pages doesn't repeat it. See [currency-conversions.md](currency-conversions.md#5-caching) for what a circuit is.
- Saving on the Recurring page, or ticking "Repeat every month" on an expense, also runs it straight away.

### 2. `NextDate` is the single source of truth

Each item only remembers when it's next due. Creating an entry and moving `NextDate` forward are **saved together in one database transaction**, so an entry can never be created without the date moving on, or the other way round. That also makes it safe to run any number of times: it only does anything when something is actually due.

### 3. Duplicates are blocked by the database

If two tabs open at the same moment, both could try to create the same entry. A **unique index on (`RecurringExpenseId`, `Date`)** means the database rejects the second one, and the code treats that as "another tab already did it". Because the rule is enforced in the database, it holds even when requests race each other.

### 4. Failures wait for next time

If the exchange rate can't be fetched, the item **stays due** and is created next time the app is opened. Opening the app never fails because of recurring items.

## Behaviour

| Situation | What happens |
|---|---|
| Start date in the past | The missed months are added straight away. |
| Day 29–31 in a shorter month | The entry falls on the month's last day. |
| Editing an item | Only future entries change. Changing the day or start date restarts the schedule from today, without re-adding past months. |
| Pausing, then resuming | Nothing is created while paused, and resuming doesn't add the paused months. |
| Deleting an item | Expenses it already created are kept (their link is cleared). |
| Category or account used by an item | It can't be deleted. |

## Making an existing expense recurring

The "Repeat every month on day N" checkbox on the expense form reuses everything above rather than adding new logic:

1. `RecurringExpenseService.FromExpense` copies the expense into a new `RecurringExpense`, with `DayOfMonth` taken from the expense's date and `NextDate` set **a month later**, because the expense itself is the first entry.
2. The expense and the new item are **saved together**, with the expense linked as entry #1.
3. The **same engine** (`CreateDueAsync`) then runs, so an older expense automatically gets the months since added.

Once an expense is recurring, its form shows "↻ This expense repeats every month" with a link to the Recurring page.

## The flow

```
Open the app (Expenses or Summary page)
   → RecurringExpenseService.RunOnceAsync()   (once per tab)
       → items where Active and NextDate ≤ today
           → for each due date:
               create expense (converted to CAD + AED)
               NextDate = next month           ┐ one transaction
               save                            ┘ (unique index blocks duplicates)
```

## Tests

- [`MonthlyScheduleTests.cs`](../tests/ExpenseTracker.Tests/MonthlyScheduleTests.cs): short months, the year boundary, and whether today counts as due.
- [`RecurringExpenseServiceTests.cs`](../tests/ExpenseTracker.Tests/RecurringExpenseServiceTests.cs): catching up, running twice creating nothing new, paused items being skipped, a missing rate leaving the item due, building an item from an expense, and making an old expense recurring.

They use an in-memory database and a **fake clock**, so "today" is fixed and the results don't depend on the date the tests run.

## Talking points

- **Recurring expenses are templates with a next-due date**, processed lazily when the app opens, because the free host sleeps and can't run timers.
- **They catch up** on any months missed while the app wasn't opened.
- **Each entry is created in the same transaction that moves the due date on**, and a **unique index** stops duplicates if two tabs run at once.
- **Making an existing expense recurring** just creates a template from it and reuses the same engine.
