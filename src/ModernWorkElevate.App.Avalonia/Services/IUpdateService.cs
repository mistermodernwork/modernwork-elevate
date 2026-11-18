namespace ModernWorkElevate.AppAvalonia.Services;

/// <summary>
/// Asks GitHub whether a newer release of this app exists. Checks only: the app never
/// downloads or runs an installer — it runs from a per-machine MSI that needs an
/// administrator, and organizations roll updates out themselves.
/// </summary>
public interface IUpdateService
{
    /// <summary>The running version as <c>major.minor.patch</c>.</summary>
    string CurrentVersion { get; }

    /// <summary>
    /// Reads GitHub's latest release. <c>Update</c> is non-null only for
    /// <see cref="UpdateCheckOutcome.UpdateAvailable"/>; every other outcome says why there
    /// is nothing to offer, which a manual check shows verbatim. Never throws.
    /// </summary>
    Task<(UpdateCheckOutcome Outcome, UpdateCheckResult? Update)> CheckAsync(CancellationToken ct = default);
}
