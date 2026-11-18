namespace ModernWorkElevate.Core.Updates;

/// <summary>
/// Who decides whether the app asks GitHub for a newer version. The machine policy wins;
/// without one, the user's own switch — off until they turn it on, because the check sends
/// the device's IP address to a third party (GDPR Art. 25, data protection by default).
/// </summary>
public static class UpdateCheckRules
{
    /// <summary>Whether the daily background check runs.</summary>
    /// <param name="policy">The machine policy's <c>UpdateCheck</c>: forced on, forced off, or <c>null</c>.</param>
    /// <param name="userEnabled">The user's switch in Settings.</param>
    public static bool IsAutomaticCheckEnabled(bool? policy, bool userEnabled) => policy ?? userEnabled;

    /// <summary>
    /// Whether the "Check now" button may contact GitHub. A press is the user's consent for
    /// that one check; only a policy that forbids the check overrides it.
    /// </summary>
    public static bool IsManualCheckAllowed(bool? policy) => policy != false;
}
