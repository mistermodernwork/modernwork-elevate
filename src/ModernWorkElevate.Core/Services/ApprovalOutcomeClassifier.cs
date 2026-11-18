namespace ModernWorkElevate.Core.Services;

using ModernWorkElevate.Core.Models;

/// <summary>
/// Decides what became of a request that left the pending list — the one judgement the
/// approval watch has to get right, because "approved" tells someone they may now act
/// with the role.
/// </summary>
public static class ApprovalOutcomeClassifier
{
    /// <summary>Classifies a request that is no longer listed as pending.</summary>
    /// <param name="status">
    /// The request's own status read back from the service, or <c>null</c> when it could
    /// not be read.
    /// </param>
    /// <param name="isActiveNow">Whether the same role is active for the account right now.</param>
    /// <param name="isWithinGracePeriod">
    /// Whether the request was made moments ago in this app. Graph's read side lags the
    /// POST, so a fresh request can be missing from both the list and the lookup.
    /// </param>
    public static ApprovalOutcome Classify(ActivationStatus? status, bool isActiveNow, bool isWithinGracePeriod)
        => status switch
        {
            ActivationStatus.PendingApproval => ApprovalOutcome.StillPending,

            // Approved: active now, scheduled, or between approval and provisioning.
            ActivationStatus.Provisioned or ActivationStatus.Granted or ActivationStatus.PendingScheduleCreation
                => ApprovalOutcome.Approved,
            ActivationStatus.Denied => ApprovalOutcome.Denied,
            ActivationStatus.Canceled => ApprovalOutcome.Canceled,
            ActivationStatus.TimedOut => ApprovalOutcome.Expired,
            ActivationStatus.Failed => ApprovalOutcome.Failed,

            // No readable status: an approval-gated role that is live was approved.
            _ when isActiveNow => ApprovalOutcome.Approved,
            _ when isWithinGracePeriod => ApprovalOutcome.StillPending,
            _ => ApprovalOutcome.Closed,
        };
}
