namespace ModernWorkElevate.Core.Diagnostics;

/// <summary>
/// Outcome of a single endpoint probe. Any HTTP response from the endpoint —
/// including 4xx — counts as <see cref="Reachable"/>; a 407 comes from the proxy,
/// not the endpoint, and is <see cref="ProxyAuthenticationRequired"/>. The other
/// failure states classify what went wrong below the HTTP layer.
/// </summary>
public enum NetworkProbeStatus
{
    /// <summary>An HTTP response arrived; the endpoint is reachable.</summary>
    Reachable,

    /// <summary>The host name did not resolve — DNS is blocked or filtered.</summary>
    DnsFailure,

    /// <summary>
    /// The TCP connection was refused or the network is unreachable — typical
    /// for a firewall that actively resets blocked destinations.
    /// </summary>
    ConnectFailure,

    /// <summary>
    /// No response within the per-probe timeout — typical for a firewall that
    /// silently drops packets. Covers both connect and response timeouts.
    /// </summary>
    Timeout,

    /// <summary>
    /// The TLS handshake failed — with a corporate proxy in the path this is
    /// usually TLS inspection; the captured certificate issuer names it.
    /// </summary>
    TlsFailure,

    /// <summary>
    /// The proxy answered HTTP 407 even after the app offered the Windows logon session:
    /// the proxy wants a credential Windows cannot give it silently, or does not accept
    /// Kerberos/NTLM for this device.
    /// </summary>
    ProxyAuthenticationRequired,
}
