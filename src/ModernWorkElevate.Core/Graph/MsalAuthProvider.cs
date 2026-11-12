namespace ModernWorkElevate.Core.Graph;

using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using ModernWorkElevate.Core.Auth;

/// <summary>
/// Kiota authentication provider that bridges the Graph SDK to <see cref="IAuthService"/>.
/// Every token request is pinned to the (identity, tenant, cloud) enrollment via
/// <see cref="IAuthService.AcquireTokenForAccountAsync"/>. A Conditional Access
/// claims challenge surfaced by Kiota is forwarded to MSAL.
/// </summary>
public sealed class MsalAuthProvider : IAuthenticationProvider
{
    private const string ClaimsKey = "claims";
    private readonly IAuthService _authService;
    private readonly string[] _scopes;
    private readonly string _accountId;
    private readonly string _tenantId;
    private readonly EntraCloud _cloud;

    public MsalAuthProvider(
        IAuthService authService,
        string[] scopes,
        string accountId,
        string tenantId,
        EntraCloud cloud)
    {
        _authService = authService;
        _scopes = scopes;
        _accountId = accountId;
        _tenantId = tenantId;
        _cloud = cloud;
    }

    /// <inheritdoc />
    public async Task AuthenticateRequestAsync(
        RequestInformation request,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? claims = null;
        if (additionalAuthenticationContext is not null
            && additionalAuthenticationContext.TryGetValue(ClaimsKey, out var value))
        {
            claims = value as string;
        }

        // A server-sent challenge (Kiota retry context) wins over the proactive
        // per-request option stamped by the PIM services for auth-context policies.
        claims ??= request.RequestOptions.OfType<AuthContextRequestOption>().FirstOrDefault()?.ClaimsJson;

        // A background read renews silently and reports "sign-in required" instead of
        // opening a window nobody asked for (InteractiveAuthScope). Claims come only with
        // an activation — a step-up the user is waiting for — so they may prompt.
        var silentOnly = claims is null && !InteractiveAuthScope.IsAllowed;
        var result = await _authService
            .AcquireTokenForAccountAsync(_accountId, _tenantId, _cloud, _scopes, claims, silentOnly, cancellationToken)
            .ConfigureAwait(false);

        request.Headers.Add("Authorization", $"Bearer {result.AccessToken}");
    }
}
