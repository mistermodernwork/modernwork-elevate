using AppForge.Core.Domain;

namespace AppForge.Core.Abstractions;

public sealed record SigningRequest(
    IReadOnlyList<string> Files,
    SigningCertificateEntity Certificate,
    string? Description = null,
    string? DescriptionUrl = null);

public sealed record SignedFileResult(string File, bool Success, string? Error, string? Thumbprint);

public sealed record SigningResult(bool Success, IReadOnlyList<SignedFileResult> Files, string Log);

/// <summary>Signiert Dateien mit einem bestimmten Zertifikat (Plan 4.1, F3).</summary>
public interface ISigningProvider
{
    SigningProviderType Type { get; }

    Task<SigningResult> SignAsync(SigningRequest request, CancellationToken ct = default);

    /// <summary>Prüft, ob das Zertifikat erreichbar und gültig ist, und liefert Metadaten.</summary>
    Task<CertificateInfo> InspectAsync(SigningCertificateEntity certificate, CancellationToken ct = default);
}

public sealed record CertificateInfo(bool Reachable, string Subject, string Issuer, string Thumbprint, DateTimeOffset? NotAfter, bool HasCodeSigningEku, string? Error);

public enum SignatureStatus
{
    Valid,
    NotSigned,
    HashMismatch,
    UntrustedRoot,
    Expired,
    Unknown,
}

public sealed record SignatureInfo(
    SignatureStatus Status,
    string? Subject,
    string? Issuer,
    string? Thumbprint,
    bool HasTimestamp,
    string? StatusMessage);

/// <summary>Prüft Authenticode-Signaturen (Windows: Get-AuthenticodeSignature / signtool verify /pa).</summary>
public interface ISignatureVerifier
{
    Task<SignatureInfo> VerifyAsync(string file, CancellationToken ct = default);
}

public sealed record ScanResult(bool Clean, string Engine, string Details);

public interface IMalwareScanner
{
    Task<ScanResult> ScanAsync(string path, CancellationToken ct = default);
}

public sealed record AnalyzerFinding(string RuleName, string Severity, int Line, string Message);

public interface IScriptAnalyzer
{
    Task<IReadOnlyList<AnalyzerFinding>> AnalyzeAsync(string scriptPath, CancellationToken ct = default);
}
