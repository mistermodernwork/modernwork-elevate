namespace ModernWorkElevate.AppAvalonia.Services;

using Microsoft.Extensions.Logging;

/// <summary>
/// The app never updates itself. Earlier versions downloaded and applied
/// the installer packages from GitHub Releases; here the app runs from a per-machine MSI
/// install in Program Files that a standard user cannot write to, and updates are
/// rolled out by the organization (SCCM/Intune, MSI major upgrade). This
/// implementation keeps the <see cref="IUpdateService"/> contract so the existing
/// UI and controller code stays unchanged: <see cref="IsSupported"/> is always
/// false, which hides every update affordance, and no network call is ever made.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    public UpdateService(ILogger<UpdateService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        logger.LogDebug("Self-update is disabled in this build; updates come from the MSI rollout");
    }

    /// <inheritdoc />
    public bool IsSupported => false;

    /// <inheritdoc />
    public string? CurrentVersion => null;

    /// <inheritdoc />
    public Task<(UpdateCheckOutcome Outcome, UpdateCheckResult? Update)> CheckAsync(CancellationToken ct = default)
        => Task.FromResult<(UpdateCheckOutcome, UpdateCheckResult?)>((UpdateCheckOutcome.NotSupported, null));

    /// <inheritdoc />
    public Task DownloadAsync(UpdateCheckResult update, Action<int> onProgress, CancellationToken ct = default)
        => throw new NotSupportedException("Self-update is disabled in this build.");

    /// <inheritdoc />
    public void ApplyAndRestart(UpdateCheckResult update)
        => throw new NotSupportedException("Self-update is disabled in this build.");

    /// <inheritdoc />
    public void ApplyOnNextLaunch(UpdateCheckResult update)
        => throw new NotSupportedException("Self-update is disabled in this build.");
}
