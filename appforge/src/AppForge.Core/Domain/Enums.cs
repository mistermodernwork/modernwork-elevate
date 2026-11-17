namespace AppForge.Core.Domain;

/// <summary>Status eines Software-Profils bzw. einer Software-Version im Katalog.</summary>
public enum CatalogStatus
{
    New,
    InTest,
    Approved,
    Published,
    Deprecated,
    Blocked,
}

public enum VersionMode
{
    /// <summary>Immer die neueste Version der Quelle.</summary>
    Latest,

    /// <summary>Genau eine fest vorgegebene Version.</summary>
    Pinned,

    /// <summary>Mehrere ausgewählte Versionen (z. B. N und N-1).</summary>
    Multiple,

    /// <summary>Ringe: z. B. Pilot = Latest, Produktion = Latest-1.</summary>
    Ring,
}

public enum DeploymentPhase
{
    PreInstall,
    Install,
    PostInstall,
    PreUninstall,
    Uninstall,
    PostUninstall,
    PreRepair,
    Repair,
    PostRepair,
}

public enum InstallerType
{
    Unknown,
    Msi,
    Msix,
    Exe,
    Inno,
    Nullsoft,
    InstallShield,
    WixBurn,
    Squirrel,
    AdvancedInstaller,
    Zip,
    PowerShell,
}

public enum ArtifactKind
{
    SourceInstaller,
    Package,
    IntuneWin,
    Manifest,
    Report,
    SignedFile,
    Policy,
    Log,
}

public enum BuildStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

public enum TestScenario
{
    Install,
    Detect,
    Uninstall,
    DetectAbsent,
    Repair,
}

public enum TestResult
{
    NotRun,
    Passed,
    Failed,
    Skipped,
}

public enum SigningJobType
{
    Script,
    Remediation,
    PlatformScript,
    ComplianceScript,
    Package,
    Binary,
    Policy,
}

public enum SigningJobStatus
{
    Submitted,
    Checking,
    CheckFailed,
    PendingApproval,
    Rejected,
    Signing,
    Signed,
    Failed,
}

public enum SigningProviderType
{
    /// <summary>Windows-Zertifikatsspeicher (ADCS, Smartcard, HSM über KSP).</summary>
    CertStore,

    /// <summary>Azure Key Vault / Managed HSM (CSR z. B. von ADCS signiert).</summary>
    AzureKeyVault,

    /// <summary>Azure Artifact Signing (vormals Trusted Signing).</summary>
    ArtifactSigning,

    /// <summary>Externe/öffentliche CA über Plug-in.</summary>
    ExternalCa,

    /// <summary>PFX-Datei – nur Labor/Test.</summary>
    Pfx,
}

[Flags]
public enum CertificatePurpose
{
    None = 0,
    Packages = 1,
    Scripts = 2,
    Remediations = 4,
    Policies = 8,
    Test = 16,
}

public enum SourceProviderType
{
    Winget,
    Evergreen,
    GitHubRelease,
    Url,
    FileShare,
    Upload,
}

public enum PublishTargetType
{
    FileShare,
    GitHub,
    AzureDevOps,
    AzureBlob,
    Intune,
    ConfigMgr,
}

public enum DeploymentRing
{
    Test,
    Pilot,
    Production,
}

/// <summary>Regelebene für App Control for Business (WDAC).</summary>
public enum AppControlRuleLevel
{
    Publisher,
    FilePublisher,
    Hash,
}

public enum PolicyEnforcementMode
{
    Audit,
    Enforced,
}

public enum NotificationChannelType
{
    Email,
    Teams,
    LogicApp,
    Webhook,
}

public enum ApprovalSubjectType
{
    SigningJob,
    Build,
    Publication,
    Profile,
    BuildingBlock,
    Certificate,
    AppControlPolicy,
    PlatformSetting,
}
