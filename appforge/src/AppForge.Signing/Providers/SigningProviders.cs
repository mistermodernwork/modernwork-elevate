using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using AppForge.Core.Abstractions;
using AppForge.Core.Domain;
using AppForge.Core.Processes;
using AppForge.Signing.Engines;
using Azure.Core;
using Azure.Security.KeyVault.Certificates;

namespace AppForge.Signing.Providers;

/// <summary>Gemeinsame Ausführung von jsign/signtool und Ergebnisaufbereitung.</summary>
public abstract class SigningProviderBase(IProcessRunner runner, SigningOptions options) : ISigningProvider
{
    public abstract SigningProviderType Type { get; }

    protected IProcessRunner Runner { get; } = runner;

    protected SigningOptions Options { get; } = options;

    public abstract Task<SigningResult> SignAsync(SigningRequest request, CancellationToken ct = default);

    public abstract Task<CertificateInfo> InspectAsync(SigningCertificateEntity certificate, CancellationToken ct = default);

    protected bool UseSignTool =>
        Options.Engine == SigningEngineKind.SignTool || (Options.Engine == SigningEngineKind.Auto && OperatingSystem.IsWindows() && SignToolCommand.Find(Options.SignToolPath) is not null);

    protected async Task<SigningResult> RunAsync(ProcessSpec spec, IReadOnlyList<string> files, string? thumbprint, CancellationToken ct)
    {
        var result = await Runner.RunAsync(spec, ct).ConfigureAwait(false);
        var log = new StringBuilder().AppendLine(spec.ToDisplayString()).AppendLine(Mask(result.StandardOutput, spec)).AppendLine(Mask(result.StandardError, spec)).ToString();
        var ok = result.ExitCode == 0 && !result.TimedOut;
        var error = ok ? null : result.TimedOut ? "Zeitüberschreitung beim Signieren." : $"Signieren fehlgeschlagen (Exit {result.ExitCode}).";
        return new SigningResult(ok, files.Select(f => new SignedFileResult(f, ok, error, thumbprint)).ToList(), log);
    }

    protected string TimestampUrl(SigningCertificateEntity c) =>
        string.IsNullOrWhiteSpace(c.TimestampUrl) ? Options.DefaultTimestampUrl : c.TimestampUrl;

    private static string Mask(string text, ProcessSpec spec)
    {
        foreach (var value in (spec.Environment ?? new Dictionary<string, string>()).Values.Concat(spec.SecretArguments ?? []))
        {
            if (value.Length >= 4)
            {
                text = text.Replace(value, "***", StringComparison.Ordinal);
            }
        }

        return text;
    }
}

/// <summary>
/// On-Prem-PKI (ADCS) – Zertifikat im Windows-Zertifikatsspeicher des Workers, auch Smartcard/HSM via KSP.
/// Nur auf dem Windows-Worker nutzbar (signtool, Fallback Set-AuthenticodeSignature).
/// </summary>
public sealed class CertStoreSigningProvider(IProcessRunner runner, SigningOptions options) : SigningProviderBase(runner, options)
{
    public override SigningProviderType Type => SigningProviderType.CertStore;

    public override Task<SigningResult> SignAsync(SigningRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var thumb = request.Certificate.ProviderRef.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (!OperatingSystem.IsWindows() && Options.Engine == SigningEngineKind.Auto)
        {
            return Task.FromResult(new SigningResult(false, request.Files.Select(f => new SignedFileResult(f, false, "Zertifikatsspeicher-Signing braucht den Windows-Worker (Worker:Mode = WindowsService).", thumb)).ToList(), string.Empty));
        }

        var signTool = SignToolCommand.Find(Options.SignToolPath);
        var spec = signTool is not null && Options.Engine != SigningEngineKind.PowerShell
            ? SignToolCommand.BuildSign(signTool, new SignToolCall
            {
                Thumbprint = thumb,
                MachineStore = !string.Equals(ProviderSettings.From(request.Certificate).Get("store"), "CurrentUser", StringComparison.OrdinalIgnoreCase),
                TimestampUrl = TimestampUrl(request.Certificate),
                Description = request.Description,
                DescriptionUrl = request.DescriptionUrl,
                Files = request.Files,
            })
            : PowerShellSignCommand.Build(Options.PowerShellPath, thumb, TimestampUrl(request.Certificate), request.Files);
        return RunAsync(spec, request.Files, thumb, ct);
    }

    public override Task<CertificateInfo> InspectAsync(SigningCertificateEntity certificate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new CertificateInfo(false, certificate.Subject, certificate.Issuer, certificate.ProviderRef, certificate.NotAfter, true, "Prüfung nur auf dem Windows-Worker möglich."));
        }

        foreach (var location in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly);
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, certificate.ProviderRef, validOnly: false);
            if (found.Count > 0)
            {
                var c = found[0];
                return Task.FromResult(new CertificateInfo(c.HasPrivateKey, c.Subject, c.Issuer, c.Thumbprint, c.NotAfter, CertificateFacts.HasCodeSigningEku(c), c.HasPrivateKey ? null : "Kein privater Schlüssel."));
            }
        }

        return Task.FromResult(new CertificateInfo(false, string.Empty, string.Empty, certificate.ProviderRef, null, false, "Zertifikat nicht im Speicher gefunden."));
    }
}

/// <summary>
/// Azure Key Vault / Managed HSM. Schlüssel bleibt im HSM; jsign signiert per REST (Storetype AZUREKEYVAULT).
/// Einstellungen: <c>vaultUri</c>. ProviderRef = Zertifikatsname.
/// </summary>
public sealed class KeyVaultSigningProvider(IProcessRunner runner, SigningOptions options, TokenCredential credential) : SigningProviderBase(runner, options)
{
    public override SigningProviderType Type => SigningProviderType.AzureKeyVault;

    public override async Task<SigningResult> SignAsync(SigningRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = ProviderSettings.From(request.Certificate);
        var vault = new Uri(settings.Require("vaultUri"));
        var token = await credential.GetTokenAsync(new TokenRequestContext(["https://vault.azure.net/.default"]), ct).ConfigureAwait(false);
        var spec = JsignCommand.Build(Options.JavaPath, Options.JsignPath, new JsignCall
        {
            StoreType = "AZUREKEYVAULT",
            Keystore = vault.Host.Split('.')[0],
            Alias = request.Certificate.ProviderRef,
            StorePassword = token.Token,
            TimestampUrl = TimestampUrl(request.Certificate),
            TimestampRetries = Options.TimestampRetries,
            Name = request.Description,
            Url = request.DescriptionUrl,
            ProxyUrl = Options.ProxyUrl,
            Files = request.Files,
        });
        return await RunAsync(spec, request.Files, request.Certificate.Thumbprint, ct).ConfigureAwait(false);
    }

    public override async Task<CertificateInfo> InspectAsync(SigningCertificateEntity certificate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        try
        {
            var client = new CertificateClient(new Uri(ProviderSettings.From(certificate).Require("vaultUri")), credential);
            var kv = (await client.GetCertificateAsync(certificate.ProviderRef, ct).ConfigureAwait(false)).Value;
            using var x = X509CertificateLoader.LoadCertificate(kv.Cer);
            return new CertificateInfo(true, x.Subject, x.Issuer, x.Thumbprint, x.NotAfter, CertificateFacts.HasCodeSigningEku(x), null);
        }
        catch (Exception ex) when (ex is Azure.RequestFailedException or InvalidOperationException or Azure.Identity.AuthenticationFailedException)
        {
            return new CertificateInfo(false, string.Empty, string.Empty, string.Empty, null, false, ex.Message);
        }
    }
}

/// <summary>
/// Hybrid-Weg „Schlüssel im Key Vault, Zertifikat von der ADCS“: Key Vault erzeugt Schlüssel und CSR
/// (Aussteller „Unknown“), die ADCS signiert (Vorlage Code Signing), das Ergebnis wird zusammengeführt.
/// </summary>
public sealed class KeyVaultCsrService(TokenCredential credential)
{
    public async Task<string> CreateCsrAsync(Uri vaultUri, string certificateName, string subject, bool exportable = false, CancellationToken ct = default)
    {
        var client = new CertificateClient(vaultUri, credential);
        var policy = new CertificatePolicy(WellKnownIssuerNames.Unknown, subject)
        {
            KeyType = CertificateKeyType.RsaHsm,
            KeySize = 3072,
            Exportable = exportable,
            ReuseKey = false,
            ContentType = CertificateContentType.Pkcs12,
            EnhancedKeyUsage = { "1.3.6.1.5.5.7.3.3" },
            KeyUsage = { CertificateKeyUsage.DigitalSignature },
        };
        var operation = await client.StartCreateCertificateAsync(certificateName, policy, cancellationToken: ct).ConfigureAwait(false);
        var csr = operation.Properties.Csr;
        return "-----BEGIN CERTIFICATE REQUEST-----\n" + Convert.ToBase64String(csr, Base64FormattingOptions.InsertLineBreaks) + "\n-----END CERTIFICATE REQUEST-----\n";
    }

    public async Task MergeAsync(Uri vaultUri, string certificateName, IEnumerable<byte[]> issuedChain, CancellationToken ct = default)
    {
        var client = new CertificateClient(vaultUri, credential);
        await client.MergeCertificateAsync(new MergeCertificateOptions(certificateName, issuedChain), ct).ConfigureAwait(false);
    }

    /// <summary>certreq-Befehl für die ADCS (Vorlage „CodeSigning“ bzw. eigene Vorlage).</summary>
    public static string CertReqCommand(string caConfig, string template, string csrFile, string outFile) =>
        $"certreq -submit -attrib \"CertificateTemplate:{template}\" -config \"{caConfig}\" \"{csrFile}\" \"{outFile}\"";
}

/// <summary>
/// Azure Artifact Signing (vormals Trusted Signing) – Standard in der Cloud. Einstellungen:
/// <c>endpoint</c> (z. B. https://weu.codesigning.azure.net), <c>account</c>, <c>profile</c>.
/// Linux/Container: jsign TRUSTEDSIGNING; Windows-Worker: signtool + Dlib.
/// Blattzertifikate sind kurzlebig – WDAC-Regeln deshalb auf Aussteller-CA + EKU (siehe AppControl).
/// </summary>
public sealed class ArtifactSigningProvider(IProcessRunner runner, SigningOptions options, TokenCredential credential) : SigningProviderBase(runner, options)
{
    public override SigningProviderType Type => SigningProviderType.ArtifactSigning;

    public override async Task<SigningResult> SignAsync(SigningRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var s = ProviderSettings.From(request.Certificate);
        var endpoint = new Uri(s.Require("endpoint"));
        var account = s.Require("account");
        var profile = s.Get("profile") ?? request.Certificate.ProviderRef;

        if (UseSignTool && Options.ArtifactSigningDlibPath is not null)
        {
            var metadata = Path.Combine(Path.GetTempPath(), $"appforge-dlib-{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(metadata, JsonSerializer.Serialize(new { Endpoint = endpoint.ToString(), CodeSigningAccountName = account, CertificateProfileName = profile }), ct).ConfigureAwait(false);
            try
            {
                var spec = SignToolCommand.BuildSign(SignToolCommand.Find(Options.SignToolPath)!, new SignToolCall
                {
                    DlibPath = Options.ArtifactSigningDlibPath,
                    DlibMetadataFile = metadata,
                    TimestampUrl = TimestampUrl(request.Certificate),
                    Description = request.Description,
                    Files = request.Files,
                });
                return await RunAsync(spec, request.Files, null, ct).ConfigureAwait(false);
            }
            finally
            {
                File.Delete(metadata);
            }
        }

        var token = await credential.GetTokenAsync(new TokenRequestContext(["https://codesigning.azure.net/.default"]), ct).ConfigureAwait(false);
        var jsign = JsignCommand.Build(Options.JavaPath, Options.JsignPath, new JsignCall
        {
            StoreType = "TRUSTEDSIGNING",
            Keystore = endpoint.Host,
            Alias = account + "/" + profile,
            StorePassword = token.Token,
            TimestampUrl = TimestampUrl(request.Certificate),
            TimestampRetries = Options.TimestampRetries,
            Name = request.Description,
            Url = request.DescriptionUrl,
            ProxyUrl = Options.ProxyUrl,
            Files = request.Files,
        });
        return await RunAsync(jsign, request.Files, null, ct).ConfigureAwait(false);
    }

    public override async Task<CertificateInfo> InspectAsync(SigningCertificateEntity certificate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var s = ProviderSettings.From(certificate);
        try
        {
            _ = s.Require("endpoint");
            _ = s.Require("account");
            await credential.GetTokenAsync(new TokenRequestContext(["https://codesigning.azure.net/.default"]), ct).ConfigureAwait(false);
            return new CertificateInfo(true, certificate.Subject, certificate.Issuer, string.Empty, null, true, "Kurzlebige Blattzertifikate – Prüfung über die Signing-CA des Profils.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Azure.Identity.AuthenticationFailedException)
        {
            return new CertificateInfo(false, string.Empty, string.Empty, string.Empty, null, false, ex.Message);
        }
    }
}

/// <summary>
/// PFX – nur Labor/Test. Einstellungen: <c>pfxSecret</c> (Base64 im Secret-Speicher), <c>passwordSecret</c>, optional <c>alias</c>.
/// In Produktion gesperrt, solange <see cref="SigningOptions.AllowPfxInProduction"/> false ist.
/// </summary>
public sealed class PfxSigningProvider(IProcessRunner runner, SigningOptions options, ISecretStore secrets) : SigningProviderBase(runner, options)
{
    public override SigningProviderType Type => SigningProviderType.Pfx;

    public override async Task<SigningResult> SignAsync(SigningRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Certificate.IsProduction && !Options.AllowPfxInProduction)
        {
            return new SigningResult(false, request.Files.Select(f => new SignedFileResult(f, false, "PFX-Zertifikate sind in Produktion gesperrt (Signing:AllowPfxInProduction).", null)).ToList(), string.Empty);
        }

        var s = ProviderSettings.From(request.Certificate);
        var pfxBase64 = await secrets.GetAsync(s.Require("pfxSecret"), ct).ConfigureAwait(false) ?? throw new InvalidOperationException("PFX-Secret nicht gefunden.");
        var password = s.Get("passwordSecret") is { } pw ? await secrets.GetAsync(pw, ct).ConfigureAwait(false) : null;
        var file = Path.Combine(Path.GetTempPath(), $"appforge-{Guid.NewGuid():N}.pfx");
        await using (var fs = new FileStream(file, FileMode.CreateNew, FileAccess.Write))
        {
            await fs.WriteAsync(Convert.FromBase64String(pfxBase64), ct).ConfigureAwait(false);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        try
        {
            var spec = UseSignTool
                ? SignToolCommand.BuildSign(SignToolCommand.Find(Options.SignToolPath)!, new SignToolCall
                {
                    PfxFile = file,
                    PfxPassword = password,
                    TimestampUrl = TimestampUrl(request.Certificate),
                    Description = request.Description,
                    Files = request.Files,
                })
                : JsignCommand.Build(Options.JavaPath, Options.JsignPath, new JsignCall
                {
                    StoreType = "PKCS12",
                    Keystore = file,
                    StorePassword = password ?? string.Empty,
                    Alias = s.Get("alias"),
                    TimestampUrl = string.IsNullOrWhiteSpace(request.Certificate.TimestampUrl) ? null : request.Certificate.TimestampUrl,
                    TimestampRetries = Options.TimestampRetries,
                    Name = request.Description,
                    Url = request.DescriptionUrl,
                    Files = request.Files,
                });
            return await RunAsync(spec, request.Files, request.Certificate.Thumbprint, ct).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(file);
        }
    }

    public override async Task<CertificateInfo> InspectAsync(SigningCertificateEntity certificate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        try
        {
            var s = ProviderSettings.From(certificate);
            var pfx = await secrets.GetAsync(s.Require("pfxSecret"), ct).ConfigureAwait(false) ?? throw new InvalidOperationException("PFX-Secret nicht gefunden.");
            var password = s.Get("passwordSecret") is { } pw ? await secrets.GetAsync(pw, ct).ConfigureAwait(false) : null;
            using var x = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(pfx), password);
            return new CertificateInfo(x.HasPrivateKey, x.Subject, x.Issuer, x.Thumbprint, x.NotAfter, CertificateFacts.HasCodeSigningEku(x), null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Security.Cryptography.CryptographicException or FormatException)
        {
            return new CertificateInfo(false, string.Empty, string.Empty, string.Empty, null, false, ex.Message);
        }
    }
}

/// <summary>Plug-in für externe/öffentliche CAs mit Cloud-Signing-Dienst.</summary>
public interface IExternalCaPlugin
{
    string Name { get; }

    Task<JsignCall> PrepareAsync(SigningCertificateEntity certificate, IReadOnlyList<string> files, CancellationToken ct);
}

/// <summary>
/// Eingebautes Plug-in für alle jsign-Storetypes externer Anbieter (DigiCert ONE, SSL.com eSigner, SignPath,
/// Keyfactor SignServer, Garantir, HashiCorp Vault, PKCS#11-HSM). Einstellungen: <c>storetype</c>, <c>keystore</c>,
/// <c>alias</c>, <c>storepassSecret</c>, optional <c>keypassSecret</c>, <c>certfile</c>.
/// </summary>
public sealed class JsignExternalCaPlugin(ISecretStore secrets, SigningOptions options) : IExternalCaPlugin
{
    public static readonly string[] SupportedStoreTypes = ["DIGICERTONE", "ESIGNER", "SIGNPATH", "SIGNSERVER", "GARASIGN", "HASHICORPVAULT", "PKCS11", "GOOGLECLOUD", "AWS"];

    public string Name => "jsign";

    public async Task<JsignCall> PrepareAsync(SigningCertificateEntity certificate, IReadOnlyList<string> files, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var s = ProviderSettings.From(certificate);
        var storeType = s.Require("storetype").ToUpperInvariant();
        if (!SupportedStoreTypes.Contains(storeType))
        {
            throw new InvalidOperationException($"Storetype '{storeType}' wird nicht unterstützt.");
        }

        return new JsignCall
        {
            StoreType = storeType,
            Keystore = s.Get("keystore"),
            Alias = s.Get("alias") ?? certificate.ProviderRef,
            StorePassword = s.Get("storepassSecret") is { } sp ? await secrets.GetAsync(sp, ct).ConfigureAwait(false) : null,
            KeyPassword = s.Get("keypassSecret") is { } kp ? await secrets.GetAsync(kp, ct).ConfigureAwait(false) : null,
            CertFile = s.Get("certfile"),
            TimestampUrl = string.IsNullOrWhiteSpace(certificate.TimestampUrl) ? options.DefaultTimestampUrl : certificate.TimestampUrl,
            TimestampRetries = options.TimestampRetries,
            ProxyUrl = options.ProxyUrl,
            Files = files,
        };
    }
}

/// <summary>Externe CA über Plug-ins (Einstellung <c>plugin</c>, Standard „jsign“).</summary>
public sealed class ExternalCaSigningProvider(IProcessRunner runner, SigningOptions options, IEnumerable<IExternalCaPlugin> plugins) : SigningProviderBase(runner, options)
{
    public override SigningProviderType Type => SigningProviderType.ExternalCa;

    public override async Task<SigningResult> SignAsync(SigningRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = ProviderSettings.From(request.Certificate).Get("plugin") ?? "jsign";
        var plugin = plugins.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Externes CA-Plug-in '{name}' ist nicht installiert.");
        var call = await plugin.PrepareAsync(request.Certificate, request.Files, ct).ConfigureAwait(false);
        var spec = JsignCommand.Build(Options.JavaPath, Options.JsignPath, call with { Name = request.Description, Url = request.DescriptionUrl });
        return await RunAsync(spec, request.Files, request.Certificate.Thumbprint, ct).ConfigureAwait(false);
    }

    public override Task<CertificateInfo> InspectAsync(SigningCertificateEntity certificate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return Task.FromResult(new CertificateInfo(true, certificate.Subject, certificate.Issuer, certificate.Thumbprint, certificate.NotAfter, true, "Externe CA – Prüfung beim ersten Signiervorgang."));
    }
}

public static class CertificateFacts
{
    public const string CodeSigningEku = "1.3.6.1.5.5.7.3.3";

    public static bool HasCodeSigningEku(X509Certificate2 cert)
    {
        ArgumentNullException.ThrowIfNull(cert);
        return cert.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(e => e.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>().Any(o => o.Value == CodeSigningEku));
    }

    /// <summary>Ablaufwarnungen 60/30/7 Tage (Plan 4.1).</summary>
    public static int? ExpiryWarningThreshold(DateTimeOffset? notAfter, DateTimeOffset now)
    {
        if (notAfter is not { } na)
        {
            return null;
        }

        var days = (na - now).TotalDays;
        return days switch
        {
            < 0 => 0,
            <= 7 => 7,
            <= 30 => 30,
            <= 60 => 60,
            _ => null,
        };
    }
}

public sealed class SigningProviderFactory(IEnumerable<ISigningProvider> providers)
{
    public ISigningProvider Get(SigningProviderType type) =>
        providers.FirstOrDefault(p => p.Type == type) ?? throw new InvalidOperationException($"Signing-Provider {type} ist nicht registriert.");
}
