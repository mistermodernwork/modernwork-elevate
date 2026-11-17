using AppForge.Core.Abstractions;
using AppForge.Core.Domain;
using AppForge.Core.Security;
using AppForge.Signing.Checks;
using AppForge.Signing.Providers;

namespace AppForge.Signing;

/// <summary>Datei im Signiervorgang: lokaler Pfad im Arbeitsverzeichnis + Schlüssel im Artefaktspeicher.</summary>
public sealed record SigningInput(string LocalPath, string? StorageKey = null, string? Role = null);

public sealed record FileVerification(string File, SignatureInfo Signature);

public sealed record SigningOutcome(
    bool Success,
    PreCheckReport Checks,
    SigningResult? Signing,
    IReadOnlyList<FileVerification> Verification,
    IReadOnlyDictionary<string, string> HashesBefore,
    IReadOnlyDictionary<string, string> HashesAfter,
    string? Error);

/// <summary>
/// Ablauf aus Plan 4.1: Vorprüfung → (Freigabe, außerhalb) → Signieren → Verifikation → Bericht.
/// </summary>
public sealed class SigningPipeline(
    SigningOptions options,
    SigningProviderFactory providers,
    ISignatureVerifier verifier,
    IMalwareScanner scanner,
    IScriptAnalyzer? analyzer = null)
{
    public async Task<PreCheckReport> CheckAsync(IReadOnlyList<SigningInput> files, SigningJobType type, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        var findings = new List<CheckFinding>();
        if (files.Count == 0)
        {
            findings.Add(new("Input", CheckSeverity.Error, "Keine Dateien."));
            return new PreCheckReport(findings);
        }

        foreach (var input in files)
        {
            var name = Path.GetFileName(input.LocalPath);
            var ext = Path.GetExtension(input.LocalPath).ToLowerInvariant();
            if (!options.AllowedExtensions.Contains(ext))
            {
                findings.Add(new("FileType", CheckSeverity.Error, $"Dateityp {ext} ist nicht erlaubt.", name));
                continue;
            }

            var info = new FileInfo(input.LocalPath);
            if (info.Length > options.MaxFileSize)
            {
                findings.Add(new("FileSize", CheckSeverity.Error, $"Datei ist größer als {options.MaxFileSize / 1024 / 1024} MB.", name));
                continue;
            }

            var scan = await scanner.ScanAsync(input.StorageKey ?? input.LocalPath, ct).ConfigureAwait(false);
            findings.Add(new("MalwareScan", scan.Clean ? CheckSeverity.Info : CheckSeverity.Error, $"{scan.Engine}: {scan.Details}", name));

            var existing = await verifier.VerifyAsync(input.LocalPath, ct).ConfigureAwait(false);
            if (existing.Status != SignatureStatus.NotSigned && existing.Status != SignatureStatus.Unknown)
            {
                findings.Add(new("AlreadySigned", CheckSeverity.Warning, $"Datei ist bereits signiert ({existing.Subject}); die Signatur wird ersetzt.", name));
            }

            if (ScriptEncoding.IsScript(input.LocalPath))
            {
                if (ScriptEncoding.NormalizeToUtf8Bom(input.LocalPath))
                {
                    findings.Add(new("Encoding", CheckSeverity.Info, "Auf UTF-8 mit BOM normalisiert.", name));
                }

                findings.AddRange(SecretScanner.Scan(input.LocalPath));
                if (ext is ".ps1" or ".psm1")
                {
                    findings.AddRange(ScriptRules.Check(input.LocalPath));
                    if (analyzer is not null)
                    {
                        try
                        {
                            foreach (var f in await analyzer.AnalyzeAsync(input.LocalPath, ct).ConfigureAwait(false))
                            {
                                var severity = f.Severity switch
                                {
                                    "Error" or "ParseError" => CheckSeverity.Error,
                                    "Warning" => CheckSeverity.Warning,
                                    _ => CheckSeverity.Info,
                                };
                                findings.Add(new("PSScriptAnalyzer", severity, $"{f.RuleName}: {f.Message}", name, f.Line));
                            }
                        }
                        catch (InvalidOperationException ex)
                        {
                            findings.Add(new("PSScriptAnalyzer", CheckSeverity.Warning, ex.Message, name));
                        }
                    }
                }
            }
        }

        if (type is SigningJobType.Remediation)
        {
            var detection = files.FirstOrDefault(f => string.Equals(f.Role, "detection", StringComparison.OrdinalIgnoreCase)) ?? files[0];
            var remediation = files.FirstOrDefault(f => string.Equals(f.Role, "remediation", StringComparison.OrdinalIgnoreCase));
            findings.AddRange(IntuneScriptRules.CheckRemediationPair(detection.LocalPath, remediation?.LocalPath));
        }
        else if (type is SigningJobType.PlatformScript or SigningJobType.ComplianceScript)
        {
            foreach (var f in files)
            {
                findings.AddRange(IntuneScriptRules.CheckCommon(f.LocalPath, type == SigningJobType.PlatformScript ? "Platform" : "Compliance"));
            }
        }

        return new PreCheckReport(findings);
    }

    public async Task<SigningOutcome> SignAsync(IReadOnlyList<SigningInput> files, SigningCertificateEntity certificate, PreCheckReport checks, string? description = null, string? descriptionUrl = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(checks);
        var before = new Dictionary<string, string>();
        foreach (var f in files)
        {
            before[Path.GetFileName(f.LocalPath)] = await Hashing.Sha256FileAsync(f.LocalPath, ct).ConfigureAwait(false);
        }

        if (!checks.Passed)
        {
            return new SigningOutcome(false, checks, null, [], before, new Dictionary<string, string>(), "Vorprüfung nicht bestanden – es wird nicht signiert.");
        }

        if (!certificate.Enabled)
        {
            return new SigningOutcome(false, checks, null, [], before, new Dictionary<string, string>(), "Zertifikat ist deaktiviert.");
        }

        var provider = providers.Get(certificate.Provider);
        var result = await provider.SignAsync(new SigningRequest(files.Select(f => f.LocalPath).ToList(), certificate, description, descriptionUrl), ct).ConfigureAwait(false);
        var after = new Dictionary<string, string>();
        var verification = new List<FileVerification>();
        foreach (var f in files)
        {
            after[Path.GetFileName(f.LocalPath)] = await Hashing.Sha256FileAsync(f.LocalPath, ct).ConfigureAwait(false);
            if (result.Success)
            {
                verification.Add(new FileVerification(Path.GetFileName(f.LocalPath), await verifier.VerifyAsync(f.LocalPath, ct).ConfigureAwait(false)));
            }
        }

        var verified = result.Success && verification.All(v => v.Signature.Status == SignatureStatus.Valid);
        var error = !result.Success
            ? result.Files.FirstOrDefault(f => !f.Success)?.Error ?? "Signieren fehlgeschlagen."
            : verified ? null : "Verifikation nach dem Signieren fehlgeschlagen: " + string.Join("; ", verification.Where(v => v.Signature.Status != SignatureStatus.Valid).Select(v => $"{v.File}: {v.Signature.StatusMessage}"));
        return new SigningOutcome(verified, checks, result, verification, before, after, error);
    }
}
