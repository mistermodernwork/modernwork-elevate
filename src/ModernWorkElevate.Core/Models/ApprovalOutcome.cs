namespace ModernWorkElevate.Core.Models;

/// <summary>What became of a request that was waiting for approval.</summary>
public enum ApprovalOutcome
{
    /// <summary>Still waiting for an approver (or not readable yet).</summary>
    StillPending,

    /// <summary>An approver approved it; the role is active or about to be.</summary>
    Approved,

    /// <summary>An approver denied it.</summary>
    Denied,

    /// <summary>The request was withdrawn (by the requester or an administrator).</summary>
    Canceled,

    /// <summary>No approver responded before the request timed out.</summary>
    Expired,

    /// <summary>Approved, but provisioning failed.</summary>
    Failed,

    /// <summary>No longer pending, and the service does not say why.</summary>
    Closed,
}
