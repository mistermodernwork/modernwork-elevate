using AppForge.Core.Domain;
using AppForge.Core.Profiles;

namespace AppForge.Core.Abstractions;

public sealed record PublishContext(
    BuildEntity Build,
    SoftwareProfile Profile,
    PublishTargetEntity Target,
    string PackageFolder,
    string? IntuneWinFile,
    string ManifestJson,
    DeploymentRing? Ring,
    string? Secret);

public sealed record PublishResult(bool Success, string Location, string Message);

public interface IPublishTarget
{
    PublishTargetType Type { get; }

    Task<PublishResult> PublishAsync(PublishContext context, CancellationToken ct = default);
}

public sealed record NotificationMessage(
    string Event,
    string Title,
    string Text,
    IReadOnlyDictionary<string, string> Facts,
    string? DetailsUrl = null,
    Guid? ApprovalRequestId = null,
    string? ApproveUrl = null,
    string? RejectUrl = null,
    string? CallbackUrl = null);

public interface INotificationChannel
{
    NotificationChannelType Type { get; }

    Task SendAsync(NotificationMessage message, string settingsJson, string? secret, CancellationToken ct = default);
}

public static class NotificationEvents
{
    public const string SigningRequested = nameof(SigningRequested);
    public const string SigningCompleted = nameof(SigningCompleted);
    public const string ApprovalRequired = nameof(ApprovalRequired);
    public const string ApprovalGranted = nameof(ApprovalGranted);
    public const string ApprovalRejected = nameof(ApprovalRejected);
    public const string ApprovalEscalated = nameof(ApprovalEscalated);
    public const string NewVersionDetected = nameof(NewVersionDetected);
    public const string BuildSucceeded = nameof(BuildSucceeded);
    public const string BuildFailed = nameof(BuildFailed);
    public const string TestFailed = nameof(TestFailed);
    public const string Published = nameof(Published);
    public const string CertificateExpiring = nameof(CertificateExpiring);
    public const string PolicyUpdated = nameof(PolicyUpdated);

    public static readonly IReadOnlyList<string> All =
    [
        SigningRequested, SigningCompleted, ApprovalRequired, ApprovalGranted, ApprovalRejected, ApprovalEscalated,
        NewVersionDetected, BuildSucceeded, BuildFailed, TestFailed, Published, CertificateExpiring, PolicyUpdated,
    ];
}
