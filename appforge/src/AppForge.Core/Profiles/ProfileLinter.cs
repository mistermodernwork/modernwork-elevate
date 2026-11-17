using System.Text.RegularExpressions;
using AppForge.Core.Domain;

namespace AppForge.Core.Profiles;

public enum LintSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record LintFinding(LintSeverity Severity, string Path, string Message);

/// <summary>Globale Standards: Pflichtfelder und Namenskonventionen (Plan 4.7).</summary>
public sealed class ProfileLinter
{
    private static readonly Regex NamePart = new("^[A-Za-z0-9][A-Za-z0-9 .+&()-]*$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public List<string> RequiredMetadata { get; init; } = ["Vendor", "Product", "Owner"];

    public IReadOnlyList<LintFinding> Lint(SoftwareProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var findings = new List<LintFinding>();

        foreach (var field in RequiredMetadata)
        {
            var value = field switch
            {
                "Vendor" => profile.Metadata.Vendor,
                "Product" => profile.Metadata.Product,
                "Owner" => profile.Metadata.Owner,
                "Category" => profile.Metadata.Category,
                "License" => profile.Metadata.License,
                _ => "x",
            };
            if (string.IsNullOrWhiteSpace(value))
            {
                findings.Add(new(LintSeverity.Error, $"metadata.{char.ToLowerInvariant(field[0])}{field[1..]}", $"Pflichtfeld '{field}' fehlt."));
            }
        }

        foreach (var (path, value) in new[] { ("metadata.vendor", profile.Metadata.Vendor), ("metadata.product", profile.Metadata.Product) })
        {
            if (!string.IsNullOrWhiteSpace(value) && !NamePart.IsMatch(value))
            {
                findings.Add(new(LintSeverity.Error, path, $"'{value}' enthält unzulässige Zeichen für den Paketnamen."));
            }
        }

        if (string.IsNullOrWhiteSpace(profile.Source.Id) && profile.Source.Provider != SourceProviderType.Upload)
        {
            findings.Add(new(LintSeverity.Error, "source.id", "Quelle ohne ID/URL."));
        }

        switch (profile.Source.VersionMode)
        {
            case VersionMode.Pinned when profile.Source.Versions.Count != 1:
                findings.Add(new(LintSeverity.Error, "source.versions", "Versionsmodus 'Pinned' braucht genau eine Version."));
                break;
            case VersionMode.Multiple when profile.Source.Versions.Count == 0:
                findings.Add(new(LintSeverity.Error, "source.versions", "Versionsmodus 'Multiple' braucht mindestens eine Version oder Regel (z. B. 'latest', 'latest-1')."));
                break;
            case VersionMode.Ring when profile.Source.RingOffsets.Count == 0:
                findings.Add(new(LintSeverity.Error, "source.ringOffsets", "Versionsmodus 'Ring' braucht Ring-Abstände."));
                break;
        }

        if (profile.Detection.Kind == DetectionKind.Msi && string.IsNullOrWhiteSpace(profile.Detection.ProductCode ?? profile.Installer.ProductCode))
        {
            findings.Add(new(LintSeverity.Warning, "detection.productCode", "MSI-Erkennung ohne ProductCode – wird bei der Analyse ermittelt."));
        }

        var hasInstall = profile.Phases.TryGetValue(DeploymentPhase.Install, out var install) && install.Any(b => b.Enabled);
        var hasRepair = profile.Phases.TryGetValue(DeploymentPhase.Repair, out var repair) && repair.Any(b => b.Enabled);
        if (hasRepair && !hasInstall && profile.Installer.Type == InstallerType.Unknown)
        {
            findings.Add(new(LintSeverity.Error, "phases.repair", "Repair ohne Install-Schritt."));
        }

        if (profile.AppControl.PreferredLevel == AppControlRuleLevel.Hash)
        {
            findings.Add(new(LintSeverity.Warning, "appControl.preferredLevel", "Hash-Regeln als Standard erzeugen bei jedem Update neue Regeln (Freigabestufe 2)."));
        }

        return findings;
    }
}

/// <summary>Paketnamen-Konvention: Hersteller_Produkt_Version_Arch_Sprache_PSADT_R&lt;Revision&gt;.</summary>
public static class PackageNaming
{
    private static readonly Regex Invalid = new("[^A-Za-z0-9.+-]", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public static string Build(string vendor, string product, string version, string architecture, string language, int revision)
    {
        static string Clean(string s) => Invalid.Replace(s.Trim(), string.Empty);
        return string.Join('_', Clean(vendor), Clean(product), Clean(version), Clean(architecture), Clean(language), "PSADT", $"R{revision}");
    }
}
