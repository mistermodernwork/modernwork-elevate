namespace ModernWorkElevate.AppAvalonia.Services;

using ModernWorkElevate.Core.Models;

/// <summary>
/// Shows Windows toast notifications. Justification text is never included.
/// </summary>
public interface IToastService
{
    /// <summary>Shows the outcome of an activation request.</summary>
    void ShowActivationResult(string resourceName, ActivationResult result);

    /// <summary>Shows the outcome of a deactivation request.</summary>
    void ShowDeactivationResult(string resourceName, ActivationResult result);

    /// <summary>
    /// Shows what became of a request that was waiting for approval. Names no approver:
    /// the app reads only the request's status, never the approval itself.
    /// </summary>
    void ShowApprovalOutcome(string resourceName, ApprovalOutcome outcome);

    /// <summary>Shows a generic error notification.</summary>
    void ShowError(string title, string detail);
}
