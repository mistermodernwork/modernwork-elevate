using AppForge.Core.Domain;

namespace AppForge.Core.Profiles;

/// <summary>
/// Vollständige, reproduzierbare Beschreibung eines Produkts (Plan 4.4). Wird als YAML gespeichert,
/// versioniert und kann Vorlagen erben (globale Standards → Vorlage → Profil).
/// </summary>
public sealed class SoftwareProfile
{
    public string SchemaVersion { get; set; } = "1";

    public string? Template { get; set; }

    public ProfileMetadata Metadata { get; set; } = new();

    public SourceSettings Source { get; set; } = new();

    public InstallerSettings Installer { get; set; } = new();

    /// <summary>Bausteine je Phase (GUI-Builder).</summary>
    public Dictionary<DeploymentPhase, List<BlockInstance>> Phases { get; set; } = [];

    public DetectionSettings Detection { get; set; } = new();

    public RequirementSettings Requirements { get; set; } = new();

    public SigningSettings Signing { get; set; } = new();

    public ApprovalSettings Approval { get; set; } = new();

    public List<string> PublishTargets { get; set; } = [];

    public IntuneSettings Intune { get; set; } = new();

    public AppControlSettings AppControl { get; set; } = new();

    public PsadtSettings Psadt { get; set; } = new();
}

public sealed class ProfileMetadata
{
    public string Vendor { get; set; } = string.Empty;

    public string Product { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Owner { get; set; } = string.Empty;

    public string License { get; set; } = string.Empty;

    public string Criticality { get; set; } = "Normal";

    public string Description { get; set; } = string.Empty;

    public string InformationUrl { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];

    public string Language { get; set; } = "MUI";
}

public sealed class SourceSettings
{
    public SourceProviderType Provider { get; set; } = SourceProviderType.Winget;

    /// <summary>winget-ID, Evergreen-App-Name, GitHub "owner/repo", URL-Muster oder Share-Pfad.</summary>
    public string Id { get; set; } = string.Empty;

    public VersionMode VersionMode { get; set; } = VersionMode.Latest;

    public List<string> Versions { get; set; } = [];

    /// <summary>Für <see cref="VersionMode.Ring"/>: Ring → Abstand zur neuesten Version (0 = neueste).</summary>
    public Dictionary<DeploymentRing, int> RingOffsets { get; set; } = new()
    {
        [DeploymentRing.Pilot] = 0,
        [DeploymentRing.Production] = 1,
    };

    public string Architecture { get; set; } = "x64";

    public string? Channel { get; set; }

    public string? InstallerLocale { get; set; }

    /// <summary>Regex für <see cref="SourceProviderType.GitHubRelease"/>-Assets oder Versionsextraktion bei URL-Quellen.</summary>
    public string? AssetPattern { get; set; }

    public string? VersionPattern { get; set; }

    public string? Sha256 { get; set; }

    /// <summary>Prüfintervall für neue Versionen (Stunden).</summary>
    public int CheckIntervalHours { get; set; } = 24;
}

public sealed class InstallerSettings
{
    public InstallerType Type { get; set; } = InstallerType.Unknown;

    public string? FileName { get; set; }

    public string? SilentArguments { get; set; }

    public string? UninstallArguments { get; set; }

    public string? Transform { get; set; }

    public string? ProductCode { get; set; }

    public string? UpgradeCode { get; set; }

    public List<string> ProcessesToClose { get; set; } = [];

    public List<string> Dependencies { get; set; } = [];

    public List<int> SuccessExitCodes { get; set; } = [0, 3010, 1641];

    public List<int> RebootExitCodes { get; set; } = [3010, 1641];
}

public sealed class BlockInstance
{
    /// <summary>Baustein-ID, z. B. <c>registry.set-value</c>.</summary>
    public string Block { get; set; } = string.Empty;

    /// <summary>Optional feste Bausteinversion; leer = aktuell freigegebene.</summary>
    public string? Version { get; set; }

    public bool Enabled { get; set; } = true;

    public string? Label { get; set; }

    public Dictionary<string, string> Parameters { get; set; } = [];
}

public enum DetectionKind
{
    Msi,
    Registry,
    File,
    Script,
}

public sealed class DetectionSettings
{
    public DetectionKind Kind { get; set; } = DetectionKind.Registry;

    public string? ProductCode { get; set; }

    public string? RegistryKey { get; set; }

    public string? ValueName { get; set; }

    public string? Path { get; set; }

    public string? FileOrFolder { get; set; }

    /// <summary>Vergleichsoperator für Versionen: <c>GreaterThanOrEqual</c>, <c>Equal</c> …</summary>
    public string Operator { get; set; } = "GreaterThanOrEqual";

    public string? Value { get; set; }

    public string? Script { get; set; }

    public bool Check32BitOn64 { get; set; }
}

public sealed class RequirementSettings
{
    public List<string> Architectures { get; set; } = ["x64"];

    public string MinimumOs { get; set; } = "Windows11_23H2";

    public int MinimumFreeDiskMb { get; set; } = 500;

    public int MinimumMemoryMb { get; set; }
}

public sealed class SigningSettings
{
    /// <summary>Zertifikat-ID oder Anzeigename je Zweck; leer = Standard je Zweck.</summary>
    public string? PackageCertificate { get; set; }

    public string? ScriptCertificate { get; set; }

    public bool SignToolkitFiles { get; set; } = true;

    public bool SignUnsignedVendorBinaries { get; set; }
}

public sealed class ApprovalSettings
{
    /// <summary>Optional: Mindeststufe für dieses Profil (z. B. kritische Software immer ≥ 2).</summary>
    public int? MinimumLevel { get; set; }

    public List<string> NotifyChannels { get; set; } = [];
}

public sealed class IntuneSettings
{
    public string? DisplayName { get; set; }

    public string InstallExperience { get; set; } = "system";

    public string RestartBehavior { get; set; } = "basedOnReturnCode";

    public int MaxRunTimeMinutes { get; set; } = 60;

    public bool SupersedePrevious { get; set; } = true;

    public bool UninstallPreviousOnSupersede { get; set; }

    public Dictionary<DeploymentRing, List<string>> Assignments { get; set; } = [];
}

public sealed class AppControlSettings
{
    public AppControlRuleLevel PreferredLevel { get; set; } = AppControlRuleLevel.Publisher;

    public AppControlRuleLevel Fallback { get; set; } = AppControlRuleLevel.Hash;

    public bool CreateSupplementalPolicy { get; set; } = true;

    public string? PolicyGroup { get; set; }
}

public sealed class PsadtSettings
{
    public string ToolkitVersion { get; set; } = "4.1.7";

    public string DeployMode { get; set; } = "Auto";

    public bool AllowDefer { get; set; } = true;

    public int DeferTimes { get; set; } = 3;

    public int CloseAppsCountdownSeconds { get; set; } = 600;

    public bool CheckDiskSpace { get; set; } = true;

    public bool PersistPrompt { get; set; }
}
