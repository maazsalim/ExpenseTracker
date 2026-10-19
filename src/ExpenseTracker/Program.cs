using ExpenseTracker.Components;
using ExpenseTracker.Data;
using ExpenseTracker.Services;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Scoped (one per request / circuit) so each context knows the signed-in user.
builder.Services.AddDbContextFactory<AppDbContext>(
    opt => opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")),
    ServiceLifetime.Scoped);

// Who's signed in, for the per-user data filter.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
builder.Services.AddScoped<CircuitHandler, UserCircuitHandler>();

// Sign-in: ASP.NET Core Identity with cookies. There's no sign-up; see OwnerAccount.
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.AddAuthorization();
builder.Services.AddIdentityCore<IdentityUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 10;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/login";
    // "Keep me signed in": 30 days, renewed while the app is used.
    o.ExpireTimeSpan = TimeSpan.FromDays(30);
    o.SlidingExpiration = true;
});

// Keys that protect sign-in cookies live in the database, so restarts don't sign anyone out.
builder.Services.AddDataProtection()
    .SetApplicationName("ExpenseTracker")
    .PersistKeysToDbContext<AppDbContext>();

builder.Services.Configure<CurrencyOptions>(builder.Configuration.GetSection(CurrencyOptions.Section));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache(); // today's rate, shared app-wide (SecondaryCurrencyRate)
builder.Services.AddHttpClient<IUsdRateSource, FrankfurterRateSource>(http =>
{
    http.BaseAddress = new Uri("https://api.frankfurter.dev/");
    // Don't keep the user waiting long; ExchangeRateService falls back to a recent saved rate.
    http.Timeout = TimeSpan.FromSeconds(6);
});
builder.Services.AddScoped<IExchangeRateService, ExchangeRateService>();
builder.Services.AddScoped<ExpenseConverter>();
builder.Services.AddScoped<SecondaryCurrencyRate>();
builder.Services.AddScoped<DisplayCurrency>();
builder.Services.AddScoped<RecurringExpenseService>();
builder.Services.AddScoped<PendingExpenseService>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Sign out. A form post ([FromForm]) so it's protected by the antiforgery check.
app.MapPost("/logout", async (SignInManager<IdentityUser> signIn, [FromForm] string? returnUrl) =>
{
    await signIn.SignOutAsync();
    return TypedResults.LocalRedirect("~/login");
});

await OwnerAccount.EnsureAsync(app.Services);

app.Run();
