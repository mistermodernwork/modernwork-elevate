namespace AppForge.Core.Domain;

/// <summary>
/// App-Rollen der Entra-App-Registrierung (Werte = <c>value</c> der App-Rolle).
/// </summary>
public static class AppRoles
{
    public const string Engineer = "AppForge.Engineer";
    public const string Packager = "AppForge.Packager";
    public const string Approver = "AppForge.Approver";
    public const string Security = "AppForge.Security";
    public const string ChangeBoard = "AppForge.ChangeBoard";
    public const string SigningAdmin = "AppForge.SigningAdmin";
    public const string PlatformAdmin = "AppForge.PlatformAdmin";
    public const string Auditor = "AppForge.Auditor";

    /// <summary>Service Principals (Pipelines, Logic Apps). Dürfen einreichen, nie freigeben.</summary>
    public const string Automation = "AppForge.Automation";

    public static readonly IReadOnlyList<string> All =
    [
        Engineer, Packager, Approver, Security, ChangeBoard, SigningAdmin, PlatformAdmin, Auditor, Automation,
    ];
}

/// <summary>Benannte Autorisierungsrichtlinien der API.</summary>
public static class Policies
{
    public const string CanRead = nameof(CanRead);
    public const string CanSubmitSigning = nameof(CanSubmitSigning);
    public const string CanPackage = nameof(CanPackage);
    public const string CanApprove = nameof(CanApprove);
    public const string CanManageCertificates = nameof(CanManageCertificates);
    public const string CanAdminister = nameof(CanAdminister);
    public const string CanAudit = nameof(CanAudit);
}
