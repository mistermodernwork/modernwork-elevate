namespace ModernWorkElevate.Core.Services;

using ModernWorkElevate.Core.Models;

/// <summary>
/// Maps the Graph or Azure Resource Manager schedule-request <c>status</c> string
/// to <see cref="ActivationStatus"/>. Both surfaces share the vocabulary; ARM adds
/// a few in-flight states for a request it has accepted but not yet provisioned,
/// which the UI treats like a pending schedule creation.
/// </summary>
internal static class ActivationStatusParser
{
    public static ActivationStatus Parse(string? status) => status switch
    {
        "Provisioned" => ActivationStatus.Provisioned,

        // ARM reports an approver's decision on its own before it provisions.
        "Granted" or "AdminApproved" => ActivationStatus.Granted,
        "PendingApproval" or "PendingApprovalProvisioning" => ActivationStatus.PendingApproval,
        "PendingScheduleCreation" or "Accepted" or "PendingEvaluation" or "PendingProvisioning"
            or "ProvisioningStarted" or "ScheduleCreated" => ActivationStatus.PendingScheduleCreation,
        "Denied" or "AdminDenied" => ActivationStatus.Denied,
        "Failed" => ActivationStatus.Failed,
        "Revoked" => ActivationStatus.Revoked,
        "Canceled" => ActivationStatus.Canceled,
        "TimedOut" => ActivationStatus.TimedOut,
        _ => ActivationStatus.Unknown,
    };
}
