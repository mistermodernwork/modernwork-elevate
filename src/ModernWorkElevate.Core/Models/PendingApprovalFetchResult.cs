namespace ModernWorkElevate.Core.Models;

/// <summary>
/// The pending approval requests of one account, plus which surfaces could not be read.
/// </summary>
/// <param name="Items">The requests still waiting for an approver, from the surfaces that were read.</param>
/// <param name="LoadError">Set when no surface of the account could be read at all.</param>
/// <param name="UnreadKinds">
/// Kinds whose surface failed. The caller keeps the rows of those kinds it showed before:
/// a failed read is not evidence that a request was decided. Each surface stands alone, so
/// a tenant that refuses one of them does not freeze the other two.
/// </param>
public sealed record PendingApprovalFetchResult(
    IReadOnlyList<PendingApprovalRequest> Items,
    string? LoadError,
    IReadOnlySet<PimResourceKind> UnreadKinds)
{
    /// <summary>Whether requests of <paramref name="kind"/> were read in this fetch.</summary>
    public bool WasRead(PimResourceKind kind) => LoadError is null && !UnreadKinds.Contains(kind);
}
