using AppForge.Core.Domain;

namespace AppForge.Core.Abstractions;

public sealed record StoredArtifact(string Key, string Sha256, long Size);

/// <summary>
/// Artefaktspeicher (Original-Installer, Pakete, Berichte). Unveränderlich: ein Schlüssel wird genau
/// einmal geschrieben. On-Prem = Dateisystem/SMB, Azure = Blob (immutable).
/// </summary>
public interface IArtifactStore
{
    Task<StoredArtifact> PutAsync(string key, Stream content, CancellationToken ct = default);

    Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);

    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    IAsyncEnumerable<string> ListAsync(string prefix, CancellationToken ct = default);
}

/// <summary>Secrets (Tokens, Webhook-URLs, PFX-Passwörter). On-Prem = DataProtection, Azure = Key Vault.</summary>
public interface ISecretStore
{
    Task<string?> GetAsync(string name, CancellationToken ct = default);

    Task SetAsync(string name, string value, CancellationToken ct = default);

    Task DeleteAsync(string name, CancellationToken ct = default);
}

public sealed record JobLease(Guid JobId, string Type, string PayloadJson, int Attempt, object? TransportState = null);

/// <summary>
/// Warteschlange für Worker-Jobs. Die Job-Tabelle ist immer die Wahrheit über den Zustand; die Queue
/// transportiert nur die Job-ID (Service Bus) oder ist die Tabelle selbst (On-Prem).
/// </summary>
public interface IJobQueue
{
    Task<Guid> EnqueueAsync(string type, string payloadJson, string? idempotencyKey = null, Guid? tenantId = null, CancellationToken ct = default);

    Task<JobLease?> ReceiveAsync(string workerId, TimeSpan leaseDuration, CancellationToken ct = default);

    Task CompleteAsync(JobLease lease, string? resultJson, CancellationToken ct = default);

    Task FailAsync(JobLease lease, string error, bool retry, CancellationToken ct = default);
}

public interface IAuditLog
{
    Task WriteAsync(string actor, string action, string subjectType, string subjectId, object? data = null, CancellationToken ct = default);
}

public static class JobTypes
{
    public const string Sign = "sign";
    public const string Build = "build";
    public const string Test = "test";
    public const string Publish = "publish";
    public const string CheckVersions = "check-versions";
    public const string Notify = "notify";
    public const string GeneratePolicy = "generate-policy";
    public const string Ping = "ping";
}

public sealed record ArtifactRef(ArtifactKind Kind, string Key, string FileName, string Sha256, long Size);
