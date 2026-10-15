using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace ExpenseTracker.Services;

/// <summary>The signed-in user's ID, used to scope every database query to their data.</summary>
public interface ICurrentUser
{
    /// <summary>Null when nobody is signed in (then no user data is visible).</summary>
    string? UserId { get; }
}

/// <summary>
/// Reads the user from the HTTP request (static rendering and prerendering) or, once the page is
/// interactive, from the circuit, where <see cref="UserCircuitHandler"/> stores it.
/// </summary>
public class CurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    private ClaimsPrincipal? circuitUser;

    public string? UserId =>
        (circuitUser ?? http.HttpContext?.User)?.FindFirstValue(ClaimTypes.NameIdentifier);

    internal void SetCircuitUser(ClaimsPrincipal user) => circuitUser = user;
}

/// <summary>
/// Copies the signed-in user into <see cref="CurrentUser"/> for each circuit (browser tab), so
/// services used by interactive components know who's signed in.
/// </summary>
public sealed class UserCircuitHandler(AuthenticationStateProvider auth, CurrentUser currentUser)
    : CircuitHandler, IDisposable
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        auth.AuthenticationStateChanged += OnAuthenticationChanged;
        return base.OnCircuitOpenedAsync(circuit, cancellationToken);
    }

    public override async Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var state = await auth.GetAuthenticationStateAsync();
        currentUser.SetCircuitUser(state.User);
    }

    private void OnAuthenticationChanged(Task<AuthenticationState> task) =>
        _ = UpdateAsync(task);

    private async Task UpdateAsync(Task<AuthenticationState> task)
    {
        var state = await task;
        currentUser.SetCircuitUser(state.User);
    }

    public void Dispose() => auth.AuthenticationStateChanged -= OnAuthenticationChanged;
}
