namespace ModernWorkElevate.Core.Services;

using ModernWorkElevate.Core.Models;

/// <summary>
/// Read and activation access to the directory-roles PIM surface for the signed-in user.
/// </summary>
public interface IPimRoleService
{
    /// <summary>Lists the directory roles the user is eligible to activate.</summary>
    Task<IReadOnlyList<PimEligibility>> GetEligibleRolesAsync(CancellationToken ct = default);

    /// <summary>Lists the directory roles currently PIM-activated for the user.</summary>
    Task<IReadOnlyList<ActiveAssignment>> GetActiveRolesAsync(CancellationToken ct = default);

    /// <summary>Submits a self-activation request for a directory-role eligibility.</summary>
    Task<ActivationResult> ActivateAsync(ActivationRequest request, CancellationToken ct = default);

    /// <summary>Submits a self-deactivation request for an active directory-role assignment.</summary>
    Task<ActivationResult> DeactivateAsync(ActiveAssignment assignment, CancellationToken ct = default);

    /// <summary>Lists the user's own activation requests that are waiting for an approver.</summary>
    Task<IReadOnlyList<PendingApprovalRequest>> GetPendingApprovalsAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads the status of one of the user's own requests, or <c>null</c> when it cannot
    /// be found (or the id is not a GUID).
    /// </summary>
    Task<ActivationStatus?> GetRequestStatusAsync(string requestId, CancellationToken ct = default);
}
