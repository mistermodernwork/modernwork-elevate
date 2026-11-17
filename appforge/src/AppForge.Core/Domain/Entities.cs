namespace AppForge.Core.Domain;

/// <summary>Basisklasse: jede Entität gehört zu einem Tenant (v1: genau einer, Standardwert).</summary>
public abstract class TenantEntity
{
    public static readonly Guid DefaultTenant = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; } = DefaultTenant;
}

public sealed class SoftwareProfileEntity : TenantEntity
{
    public string Vendor { get; set; } = string.Empty;

    public string Product { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string OwnerGroup { get; set; } = string.Empty;

    public Guid? TemplateId { get; set; }

    public bool IsTemplate { get; set; }

    public int CurrentRevision { get; set; }

    public CatalogStatus Status { get; set; } = CatalogStatus.New;

    public DateTimeOffset CreatedAt { get; set; }

    public List<ProfileRevisionEntity> Revisions { get; set; } = [];
}

public sealed class ProfileRevisionEntity : TenantEntity
{
    public Guid ProfileId { get; set; }

    public int Revision { get; set; }

    /// <summary>Vollständiges Profil als YAML (siehe <see cref="Profiles.SoftwareProfile"/>).</summary>
    public string Yaml { get; set; } = string.Empty;

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public string ChangeNote { get; set; } = string.Empty;
}

public sealed class SoftwareVersionEntity : TenantEntity
{
    public Guid ProfileId { get; set; }

    public string Version { get; set; } = string.Empty;

    public string Architecture { get; set; } = "x64";

    public string SourceUrl { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Herausgeber laut Authenticode-Signatur des Herstellers (Subject).</summary>
    public string VendorSigner { get; set; } = string.Empty;

    public DateTimeOffset DetectedAt { get; set; }

    public CatalogStatus Status { get; set; } = CatalogStatus.New;

    public DeploymentRing? Ring { get; set; }
}

public sealed class BuildingBlockEntity : TenantEntity
{
    public string Key { get; set; } = string.Empty;

    public string Version { get; set; } = "1.0.0";

    public string Yaml { get; set; } = string.Empty;

    public bool IsBuiltIn { get; set; }

    public CatalogStatus Status { get; set; } = CatalogStatus.New;

    public string? ApprovedBy { get; set; }
}

public sealed class BuildEntity : TenantEntity
{
    public Guid ProfileId { get; set; }

    public int ProfileRevision { get; set; }

    public Guid? SoftwareVersionId { get; set; }

    public string Version { get; set; } = string.Empty;

    public string? WorkerId { get; set; }

    public BuildStatus Status { get; set; } = BuildStatus.Queued;

    public string PackageName { get; set; } = string.Empty;

    public string Log { get; set; } = string.Empty;

    public string RequestedBy { get; set; } = string.Empty;

    public DateTimeOffset QueuedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public List<ArtifactEntity> Artifacts { get; set; } = [];

    public List<TestRunEntity> TestRuns { get; set; } = [];
}

public sealed class ArtifactEntity : TenantEntity
{
    public Guid? BuildId { get; set; }

    public Guid? SigningJobId { get; set; }

    public ArtifactKind Kind { get; set; }

    public string FileName { get; set; } = string.Empty;

    /// <summary>Speicherort im <see cref="Abstractions.IArtifactStore"/> (Schlüssel, nicht absolute URL).</summary>
    public string StorageKey { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public long Size { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TestRunEntity : TenantEntity
{
    public Guid BuildId { get; set; }

    public TestScenario Scenario { get; set; }

    public TestResult Result { get; set; }

    public int ExitCode { get; set; }

    public string Log { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; }
}

public sealed class SigningCertificateEntity : TenantEntity
{
    public string DisplayName { get; set; } = string.Empty;

    public SigningProviderType Provider { get; set; }

    /// <summary>
    /// Provider-spezifische Referenz: Thumbprint (CertStore), Key-Vault-Zertifikatsname,
    /// Artifact-Signing-Profil oder Secret-Name der PFX.
    /// </summary>
    public string ProviderRef { get; set; } = string.Empty;

    /// <summary>Provider-spezifische Einstellungen als JSON (z. B. Vault-URL, Endpoint, Account).</summary>
    public string SettingsJson { get; set; } = "{}";

    public string Thumbprint { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string Issuer { get; set; } = string.Empty;

    public DateTimeOffset? NotAfter { get; set; }

    public CertificatePurpose Purposes { get; set; }

    /// <summary>Entra-Gruppen (Object-IDs), die mit diesem Zertifikat signieren dürfen; leer = alle Signierberechtigten.</summary>
    public List<string> AllowedGroups { get; set; } = [];

    public bool RequiresApproval { get; set; } = true;

    public bool IsProduction { get; set; } = true;

    public bool IsDefaultFor { get; set; }

    public string TimestampUrl { get; set; } = "http://timestamp.digicert.com";

    public bool Enabled { get; set; } = true;
}

public sealed class SigningJobEntity : TenantEntity
{
    public SigningJobType Type { get; set; }

    public Guid CertificateId { get; set; }

    public string SubmittedBy { get; set; } = string.Empty;

    public bool SubmittedByServicePrincipal { get; set; }

    /// <summary>Herkunft, z. B. "ui", "cli", "pipeline:github:org/repo@refs/heads/main".</summary>
    public string Origin { get; set; } = "ui";

    /// <summary>Pipeline mit geschütztem Branch und Pflicht-Review (dann Stufe 0 möglich).</summary>
    public bool FromProtectedPipeline { get; set; }

    public SigningJobStatus Status { get; set; } = SigningJobStatus.Submitted;

    public string Comment { get; set; } = string.Empty;

    public string InputHashesJson { get; set; } = "{}";

    public string OutputHashesJson { get; set; } = "{}";

    public string CheckReportJson { get; set; } = "{}";

    public Guid? ApprovalRequestId { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public List<ArtifactEntity> Artifacts { get; set; } = [];
}

public sealed class ApprovalRequestEntity : TenantEntity
{
    public ApprovalSubjectType SubjectType { get; set; }

    public Guid SubjectId { get; set; }

    public string Title { get; set; } = string.Empty;

    public int Level { get; set; }

    public string ReasonsJson { get; set; } = "[]";

    public string SubmittedBy { get; set; } = string.Empty;

    public Approval.ApprovalState State { get; set; } = Approval.ApprovalState.Pending;

    public bool Emergency { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset DueAt { get; set; }

    public DateTimeOffset? EmergencyConfirmDueAt { get; set; }

    public bool Escalated { get; set; }

    /// <summary>Kontext für die Freigabe: Hashes, Diff, Prüf-/Testergebnisse, Regeln, Ringe (JSON).</summary>
    public string EvidenceJson { get; set; } = "{}";

    public List<ApprovalDecisionEntity> Decisions { get; set; } = [];
}

public sealed class ApprovalDecisionEntity : TenantEntity
{
    public Guid RequestId { get; set; }

    public string Approver { get; set; } = string.Empty;

    public List<string> ApproverRoles { get; set; } = [];

    public bool Approved { get; set; }

    public string Comment { get; set; } = string.Empty;

    public NotificationChannelType? Channel { get; set; }

    public string ChannelName { get; set; } = "ui";

    public bool IsEmergencyConfirmation { get; set; }

    public DateTimeOffset At { get; set; }
}

public sealed class PublishTargetEntity : TenantEntity
{
    public string Name { get; set; } = string.Empty;

    public PublishTargetType Type { get; set; }

    public string SettingsJson { get; set; } = "{}";

    /// <summary>Name des Secrets im <see cref="Abstractions.ISecretStore"/> (Token, Passwort …).</summary>
    public string? SecretRef { get; set; }

    public bool Enabled { get; set; } = true;
}

public sealed class PublicationEntity : TenantEntity
{
    public Guid BuildId { get; set; }

    public Guid TargetId { get; set; }

    public string Location { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public bool Succeeded { get; set; }

    public string Message { get; set; } = string.Empty;

    public DeploymentRing? Ring { get; set; }

    public DateTimeOffset At { get; set; }
}

public sealed class NotificationRuleEntity : TenantEntity
{
    public string Event { get; set; } = string.Empty;

    public NotificationChannelType ChannelType { get; set; }

    public string SettingsJson { get; set; } = "{}";

    public string? SecretRef { get; set; }

    /// <summary>Profil-ID oder leer = global.</summary>
    public Guid? ProfileId { get; set; }

    public bool Enabled { get; set; } = true;
}

public sealed class AppControlRuleEntity : TenantEntity
{
    public Guid ProfileId { get; set; }

    public Guid? BuildId { get; set; }

    public AppControlRuleLevel Level { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string SignerJson { get; set; } = "{}";

    public string Sha256 { get; set; } = string.Empty;

    public Guid? PolicyId { get; set; }
}

public sealed class AppControlPolicyEntity : TenantEntity
{
    public string Name { get; set; } = string.Empty;

    public Guid PolicyGuid { get; set; } = Guid.NewGuid();

    public Guid BasePolicyGuid { get; set; }

    public string Version { get; set; } = "10.0.0.0";

    public PolicyEnforcementMode Mode { get; set; } = PolicyEnforcementMode.Audit;

    public DateTimeOffset? AuditSince { get; set; }

    public DateTimeOffset? LastBlockEventAt { get; set; }

    public string Xml { get; set; } = string.Empty;

    public Guid? ProfileId { get; set; }
}

public sealed class AuditEntryEntity
{
    public long Id { get; set; }

    public Guid TenantId { get; set; } = TenantEntity.DefaultTenant;

    public DateTimeOffset At { get; set; }

    public string Actor { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string SubjectType { get; set; } = string.Empty;

    public string SubjectId { get; set; } = string.Empty;

    public string Data { get; set; } = "{}";

    public string PrevHash { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;
}

/// <summary>Job in der Warteschlange (DB-Queue on-prem; in Azure nur als Status-Spiegel).</summary>
public sealed class JobEntity : TenantEntity
{
    public string Type { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    public string? IdempotencyKey { get; set; }

    public JobState State { get; set; } = JobState.Queued;

    public int Attempts { get; set; }

    public string? LeasedBy { get; set; }

    public DateTimeOffset? LeaseUntil { get; set; }

    public DateTimeOffset EnqueuedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? Error { get; set; }

    public string? ResultJson { get; set; }
}

public enum JobState
{
    Queued,
    Leased,
    Succeeded,
    Failed,
    DeadLettered,
}

public sealed class ScheduleEntity : TenantEntity
{
    public string Name { get; set; } = string.Empty;

    public string JobType { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    public TimeSpan Interval { get; set; } = TimeSpan.FromDays(1);

    public DateTimeOffset? LastRunAt { get; set; }

    public bool Enabled { get; set; } = true;
}
