namespace ModernWorkElevate.Core.Models;

/// <summary>
/// An activation the signed-in user requested that is still waiting for an approver —
/// read back from the service, so it survives a restart and also shows requests made
/// in the portal.
/// </summary>
/// <param name="Kind">Which API surface the request belongs to.</param>
/// <param name="DisplayName">Role display name, or resolved group display name.</param>
/// <param name="ResourceId">Role definition id (directory or Azure role) or group id.</param>
/// <param name="ScopeId">Directory scope id, group id, or ARM scope — as the request carries it.</param>
/// <param name="RequestId">
/// The schedule request's id (Graph) or name (ARM). Used to read the request's final
/// status once it leaves the pending list.
/// </param>
/// <param name="RequestedAt">When the request was created, if the service says.</param>
/// <param name="RequestedDuration">The activation length asked for, if the service says.</param>
/// <param name="ScopeLabel">Human-readable scope, as on <see cref="ActiveAssignment"/>. Display only.</param>
public sealed record PendingApprovalRequest(
    PimResourceKind Kind,
    string DisplayName,
    string ResourceId,
    string ScopeId,
    string RequestId,
    DateTimeOffset? RequestedAt,
    TimeSpan? RequestedDuration,
    string? ScopeLabel = null);
