namespace ModernWorkElevate.AppAvalonia.Services;

/// <summary>
/// What an update check concluded. "No update to offer" has several causes that a user
/// pressing "Check now" must be able to tell apart: "you are up to date" while GitHub was
/// unreachable is a lie the user acts on.
/// </summary>
public enum UpdateCheckOutcome
{
    /// <summary>A newer release is published.</summary>
    UpdateAvailable,

    /// <summary>The running version is the newest published one.</summary>
    UpToDate,

    /// <summary>GitHub could not be reached or answered with nothing usable.</summary>
    Failed,

    /// <summary>The organization's policy forbids contacting GitHub.</summary>
    DisabledByPolicy,
}
