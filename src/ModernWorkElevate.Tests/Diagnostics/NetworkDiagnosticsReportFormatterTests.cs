namespace ModernWorkElevate.Tests.Diagnostics;

using ModernWorkElevate.Core.Diagnostics;

/// <summary>
/// Covers the plain-text support report — the artifact a customer pastes into
/// a ticket, so classification, issuer evidence, and the WAM caveat must all
/// survive formatting.
/// </summary>
public sealed class NetworkDiagnosticsReportFormatterTests
{
    private static readonly NetworkProbe OkProbe = new(
        new Uri("https://login.partner.microsoftonline.cn/common/v2.0/.well-known/openid-configuration"),
        "Sign-in (token endpoint)");

    private static readonly NetworkProbe TlsProbe = new(
        new Uri("https://aadcdn.msauth.cn/"),
        "Sign-in page assets (CDN)");

    [Fact]
    public void Format_Report_ContainsFailClassificationIssuerAndWamCaveat()
    {
        var report = new NetworkDiagnosticsReport(
            new DateTimeOffset(2026, 8, 13, 9, 12, 44, TimeSpan.Zero),
            [
                new NetworkGroupResult(
                    new NetworkProbeGroup("Entra China (21Vianet)", IsOptional: false, [OkProbe, TlsProbe]),
                    "http://proxy.corp.local:8080/",
                    [
                        new NetworkProbeResult(OkProbe, NetworkProbeStatus.Reachable, 200, TimeSpan.FromMilliseconds(143), null, null),
                        new NetworkProbeResult(TlsProbe, NetworkProbeStatus.TlsFailure, null, TimeSpan.FromMilliseconds(60), "CN=CorpProxy CA", null),
                    ]),
            ]);

        var text = NetworkDiagnosticsReportFormatter.Format(report, "0.5.0", "Microsoft Windows NT 10.0.22631.0");

        Assert.Contains("App v0.5.0", text);
        Assert.Contains("Microsoft Windows NT 10.0.22631.0", text);
        Assert.Contains("proxy: http://proxy.corp.local:8080/", text);
        Assert.Contains("[OK]", text);
        Assert.Contains("HTTP 200 · 143 ms", text);
        Assert.Contains("[FAIL]", text);
        Assert.Contains("TLS failure — likely TLS inspection (issuer: CN=CorpProxy CA)", text);
        Assert.Contains(NetworkDiagnosticsReportFormatter.WamCaveat, text);
    }

    [Fact]
    public void Format_NoProxyAndOptionalGroup_SaysNoneAndOptional()
    {
        var probe = new NetworkProbe(new Uri("https://api.github.com/"), "Update feed (GitHub)");
        var report = new NetworkDiagnosticsReport(
            DateTimeOffset.UnixEpoch,
            [
                new NetworkGroupResult(
                    new NetworkProbeGroup("Update feed", IsOptional: true, [probe]),
                    ProxyUri: null,
                    [new NetworkProbeResult(probe, NetworkProbeStatus.DnsFailure, null, TimeSpan.Zero, null, null)]),
            ]);

        var text = NetworkDiagnosticsReportFormatter.Format(report, "0.5.0", "os");

        Assert.Contains("== Update feed (optional) ==  proxy: none", text);
        Assert.Contains("DNS failure", text);
    }

    [Fact]
    public void Format_HostRoutedDifferentlyFromItsGroup_NamesTheRoute()
    {
        // PAC: sign-in through the proxy (tenant restrictions header), the rest direct.
        var login = new NetworkProbe(new Uri("https://login.microsoftonline.com/"), "Sign-in");
        var graph = new NetworkProbe(new Uri("https://graph.microsoft.com/"), "Microsoft Graph");
        var report = new NetworkDiagnosticsReport(
            DateTimeOffset.UnixEpoch,
            [
                new NetworkGroupResult(
                    new NetworkProbeGroup("Entra Global", IsOptional: false, [login, graph]),
                    "http://proxy.corp.local:8080/",
                    [
                        new NetworkProbeResult(login, NetworkProbeStatus.Reachable, 200, TimeSpan.Zero, null, null, "http://proxy.corp.local:8080/"),
                        new NetworkProbeResult(graph, NetworkProbeStatus.Reachable, 401, TimeSpan.Zero, null, null, ProxyUri: null),
                    ]),
            ]);

        var text = NetworkDiagnosticsReportFormatter.Format(report, "1.3.1", "os");

        var graphLine = text.Split('\n').Single(l => l.Contains("graph.microsoft.com", StringComparison.Ordinal));
        Assert.EndsWith("· direct", graphLine.TrimEnd());
        var loginLine = text.Split('\n').Single(l => l.Contains("login.microsoftonline.com", StringComparison.Ordinal));
        Assert.DoesNotContain("direct", loginLine, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NetworkProbeStatus.DnsFailure, null, "DNS failure")]
    [InlineData(NetworkProbeStatus.ProxyAuthenticationRequired, null, "Proxy requires authentication (HTTP 407)")]
    [InlineData(NetworkProbeStatus.Timeout, null, "Timeout (connect or response)")]
    [InlineData(NetworkProbeStatus.ConnectFailure, "ConnectionRefused", "Blocked (ConnectionRefused)")]
    [InlineData(NetworkProbeStatus.ConnectFailure, null, "Blocked (connection failed)")]
    public void StatusLabel_MapsFailureStatuses(NetworkProbeStatus status, string? detail, string expected)
    {
        var result = new NetworkProbeResult(TlsProbe, status, null, TimeSpan.Zero, null, detail);

        Assert.Equal(expected, NetworkDiagnosticsReportFormatter.StatusLabel(result));
    }
}
