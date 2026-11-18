namespace ModernWorkElevate.AppAvalonia.ViewModels;

using ModernWorkElevate.Core.Models;

/// <summary>
/// The words for each kind of PIM resource, shared by every row that names one so an
/// active role and a role waiting for approval are never described differently.
/// </summary>
public static class ResourceKindLabels
{
    /// <summary>"Directory role", "Group membership", … for <paramref name="kind"/>.</summary>
    public static string For(PimResourceKind kind) => kind switch
    {
        PimResourceKind.DirectoryRole => "Directory role",
        PimResourceKind.GroupMembership => "Group membership",
        PimResourceKind.GroupOwnership => "Group ownership",
        PimResourceKind.AzureResourceRole => "Azure resource role",
        _ => string.Empty,
    };
}
