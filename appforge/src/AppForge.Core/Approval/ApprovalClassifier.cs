using AppForge.Core.Domain;

namespace AppForge.Core.Approval;

/// <summary>Art des Vorgangs, für den eine Freigabestufe ermittelt wird.</summary>
public enum ApprovalOperation
{
    SignScript,
    SignBinary,
    SignRemediation,
    SignPlatformScript,
    SignComplianceScript,
    ResignForeignFiles,
    BuildAndDistribute,
    ProfileChange,
    NewProduct,
    BuildingBlockChange,
    SupplementalPolicyUpdate,
    BasePolicyChange,
    PolicyEnforce,
    CertificateChange,
    ApprovalRulesChange,
    RoleChange,
    UnblockProduct,
}

/// <summary>Alle Fakten, aus denen die Plattform die Freigabestufe automatisch ableitet.</summary>
public sealed record ApprovalContext
{
    public required ApprovalOperation Operation { get; init; }

    /// <summary>Signieren mit Test-/Lab-Zertifikat (Geräte in Produktion vertrauen ihm nicht).</summary>
    public bool UsesTestCertificate { get; init; }

    public DeploymentRing? TargetRing { get; init; }

    public bool IsNewProduct { get; init; }

    /// <summary>Profil-Revision weicht von der zuletzt freigegebenen ab.</summary>
    public bool ProfileChangedSinceLastApproval { get; init; }

    /// <summary>Profil wurde schon einmal freigegeben (Voraussetzung für Stufe 0 bei neuen Versionen).</summary>
    public bool ProfilePreviouslyApproved { get; init; }

    public bool? VendorSignatureValid { get; init; }

    public bool VendorSignerChanged { get; init; }

    /// <summary>null = Tests nicht gelaufen.</summary>
    public bool? AllTestsPassed { get; init; }

    public bool RequiresHashRules { get; init; }

    /// <summary>Eigener Code aus dem Expertenmodus des GUI-Builders.</summary>
    public bool ContainsCustomCode { get; init; }

    /// <summary>Neuer oder geänderter (noch nicht freigegebener) Baustein im Paket.</summary>
    public bool UsesUnapprovedBuildingBlock { get; init; }

    /// <summary>Pipeline mit geschütztem Branch und Pflicht-Review – das Review ist die Freigabe.</summary>
    public bool FromProtectedPipeline { get; init; }
}

public sealed record ApprovalClassification(int Level, IReadOnlyList<string> Reasons, bool Blocked, string? BlockReason)
{
    public bool RequiresApproval => !Blocked && Level > 0;
}

/// <summary>
/// Risikobasierte Einstufung in die Freigabestufen 0–3 (Plan, Abschnitt 4.5).
/// Es gewinnt immer die höchste zutreffende Regel. Stufe 0 gilt nur, wenn keine höhere Regel greift
/// und die Bedingungen für Automatik ausdrücklich erfüllt sind – sonst ist Stufe 1 der sichere Standard.
/// </summary>
public static class ApprovalClassifier
{
    public static ApprovalClassification Classify(ApprovalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.AllTestsPassed == false)
        {
            return new ApprovalClassification(0, [], true, "Mindestens ein automatischer Test ist fehlgeschlagen – das Paket wird nicht zur Freigabe gestellt.");
        }

        var level3 = new List<string>();
        var level2 = new List<string>();
        var level1 = new List<string>();
        var level0 = new List<string>();

        switch (context.Operation)
        {
            case ApprovalOperation.BasePolicyChange:
                level3.Add("Änderung der WDAC-Basisrichtlinie");
                break;
            case ApprovalOperation.PolicyEnforce:
                level3.Add("Supplemental Policy wechselt von Audit nach Enforced");
                break;
            case ApprovalOperation.CertificateChange:
                level3.Add("Signing-Zertifikat wird angebunden, getauscht oder widerrufen");
                break;
            case ApprovalOperation.ApprovalRulesChange:
                level3.Add("Änderung an Freigaberegeln");
                break;
            case ApprovalOperation.RoleChange:
                level3.Add("Änderung an Rollen");
                break;
            case ApprovalOperation.UnblockProduct:
                level3.Add("Notfall-Sperre eines Produkts wird aufgehoben");
                break;
            case ApprovalOperation.SignRemediation:
                level2.Add("Intune-Remediation (läuft als SYSTEM)");
                break;
            case ApprovalOperation.SignPlatformScript:
                level2.Add("Intune-Platform-Skript (läuft als SYSTEM)");
                break;
            case ApprovalOperation.SignComplianceScript:
                level2.Add("Custom-Compliance-Skript (läuft als SYSTEM)");
                break;
            case ApprovalOperation.ResignForeignFiles:
                level2.Add("Fremde Dateien werden neu signiert");
                break;
            case ApprovalOperation.NewProduct:
                level2.Add("Neues Produkt im Katalog");
                break;
            case ApprovalOperation.BuildingBlockChange:
                level2.Add("Neuer oder geänderter Baustein (Code wandert in jedes Paket)");
                break;
            case ApprovalOperation.ProfileChange:
                level1.Add("Änderung an einem bestehenden Profil");
                break;
            case ApprovalOperation.SupplementalPolicyUpdate:
                // Supplemental Policies gehen immer zuerst im Audit-Modus raus.
                level1.Add("Supplemental Policy (Audit-Modus) wird aktualisiert");
                break;
            case ApprovalOperation.SignScript:
            case ApprovalOperation.SignBinary:
                if (context.UsesTestCertificate)
                {
                    level0.Add("Signieren mit Test-/Lab-Zertifikat");
                }
                else if (context.FromProtectedPipeline)
                {
                    level0.Add("Pipeline mit geschütztem Branch und Pflicht-Review");
                }
                else
                {
                    level1.Add("Eigene Skripte/Binaries mit Produktivzertifikat");
                }

                break;
            case ApprovalOperation.BuildAndDistribute:
                ClassifyBuild(context, level1, level0);
                break;
            default:
                level1.Add("Unbekannter Vorgang – sicherer Standard");
                break;
        }

        // Modifikatoren, unabhängig vom Vorgang.
        if (context.IsNewProduct && context.Operation != ApprovalOperation.NewProduct)
        {
            level2.Add("Neues Produkt im Katalog");
        }

        if (context.VendorSignerChanged)
        {
            level2.Add("Herausgeber/Signer des Herstellers hat sich geändert");
        }

        if (context.VendorSignatureValid == false)
        {
            level2.Add("Herstellersignatur fehlt oder ist ungültig");
        }

        if (context.RequiresHashRules)
        {
            level2.Add("Paket benötigt Hash-Regeln (unsignierte Dateien)");
        }

        if (context.ContainsCustomCode)
        {
            level2.Add("Eigener Code im GUI-Builder (Expertenmodus)");
        }

        if (context.UsesUnapprovedBuildingBlock)
        {
            level2.Add("Paket nutzt einen nicht freigegebenen Baustein");
        }

        if (level3.Count > 0)
        {
            return new ApprovalClassification(3, [.. level3, .. level2, .. level1], false, null);
        }

        if (level2.Count > 0)
        {
            return new ApprovalClassification(2, [.. level2, .. level1], false, null);
        }

        if (level1.Count > 0)
        {
            return new ApprovalClassification(1, level1, false, null);
        }

        return level0.Count > 0
            ? new ApprovalClassification(0, level0, false, null)
            : new ApprovalClassification(1, ["Keine Automatik-Regel erfüllt – sicherer Standard"], false, null);
    }

    private static void ClassifyBuild(ApprovalContext context, List<string> level1, List<string> level0)
    {
        var ring = context.TargetRing ?? DeploymentRing.Test;
        if (ring != DeploymentRing.Test)
        {
            level1.Add($"Verteilung in den Ring {ring}");
        }

        if (context.ProfileChangedSinceLastApproval)
        {
            level1.Add("Profil wurde seit der letzten Freigabe geändert");
        }

        if (!context.ProfilePreviouslyApproved)
        {
            level1.Add("Profil wurde noch nie freigegeben");
        }

        if (context.AllTestsPassed is null)
        {
            level1.Add("Automatische Tests sind nicht gelaufen");
        }

        if (context.VendorSignatureValid is null)
        {
            level1.Add("Herstellersignatur wurde nicht geprüft");
        }

        if (ring == DeploymentRing.Test)
        {
            level0.Add("Build und Verteilung in den Test-Ring");
        }

        if (context.ProfilePreviouslyApproved && !context.ProfileChangedSinceLastApproval
            && context.VendorSignatureValid == true && !context.VendorSignerChanged && context.AllTestsPassed == true)
        {
            level0.Add("Neue Herstellerversion eines freigegebenen Profils, Signer unverändert, Tests grün");
        }
    }
}
