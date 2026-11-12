namespace ModernWorkElevate.Core.Networking;

using System.Net;

/// <summary>
/// How the app's own HTTP traffic (Microsoft Graph, Azure Resource Manager, MSAL's
/// metadata requests, the network check) passes a corporate proxy.
/// </summary>
/// <remarks>
/// <para>
/// The app never carries a proxy setting of its own. It follows the Windows proxy
/// configuration — static proxy, PAC file or WPAD — exactly like Edge and the Windows
/// account broker, so a PAC that sends Microsoft 365 and Azure traffic direct and only the
/// sign-in hosts (login.microsoftonline.com and friends) through the proxy for tenant
/// restrictions v2 header injection works without any change in the app.
/// </para>
/// <para>
/// .NET does not answer a proxy's authentication challenge by default: every request the
/// PAC routes through an authenticating proxy would end in HTTP 407. The app therefore lets
/// Windows answer it with the signed-in user's Kerberos/NTLM identity (SSPI). No password is
/// read, typed, stored or held by the app — the credentials object is a handle that tells
/// Windows to use the logon session.
/// </para>
/// </remarks>
public static class SystemProxy
{
    /// <summary>
    /// Lets every <see cref="HttpClient"/> that uses the system proxy authenticate to it
    /// with the Windows logon session. Call once at start-up, before the first request.
    /// An explicit credential someone configured stays untouched.
    /// </summary>
    public static void UseWindowsLogonForProxyAuthentication()
    {
        var proxy = HttpClient.DefaultProxy;
        proxy.Credentials ??= CredentialCache.DefaultCredentials;
    }

    /// <summary>
    /// Builds the innermost handler for the app's own clients: system proxy, answered with
    /// the Windows logon session (see <see cref="UseWindowsLogonForProxyAuthentication"/>).
    /// </summary>
    public static HttpClientHandler CreateHandler() => new()
    {
        DefaultProxyCredentials = CredentialCache.DefaultCredentials,
    };

    /// <summary>
    /// The proxy the app's process uses for <paramref name="url"/>, credentials stripped,
    /// or <c>null</c> for a direct connection — what the network check reports per endpoint,
    /// because a PAC file can route two hosts of the same service differently.
    /// </summary>
    public static string? ProxyFor(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        var defaultProxy = HttpClient.DefaultProxy;
        if (defaultProxy.IsBypassed(url))
        {
            return null;
        }

        var proxy = defaultProxy.GetProxy(url);
        if (proxy is null || proxy == url)
        {
            return null;
        }

        // Strip credentials an HTTP_PROXY value may embed — the report is
        // meant to be pasted into support tickets.
        var builder = new UriBuilder(proxy) { UserName = string.Empty, Password = string.Empty };
        return builder.Uri.ToString();
    }
}
