namespace ModernWorkElevate.Core.Auth;

/// <summary>
/// Marks the code path of something the user just asked for — Activate, Stop, Extend,
/// Refresh — as allowed to open a Windows sign-in window. Everything outside such a scope
/// (the refresh timer, the policy prefetch, a network-change reload) renews tokens silently
/// and reports "sign-in required" instead of prompting.
/// </summary>
/// <remarks>
/// Before 1.3.1 every background read could open a WAM window. Each refresh tick then
/// queued another prompt behind the process-wide auth lock, the per-account timeout
/// cancelled the window after 30 seconds, and the next tick opened the next one — windows
/// that came several times and vanished while the user was still typing.
/// The flag lives in an <see cref="AsyncLocal{T}"/>, so it follows the awaited calls of the
/// operation that set it and nothing else; it must be set inside an <c>async</c> method so
/// it is restored when that method returns.
/// </remarks>
public static class InteractiveAuthScope
{
    private static readonly AsyncLocal<bool> Allowed = new();

    /// <summary>True when the current operation may open a sign-in window.</summary>
    public static bool IsAllowed => Allowed.Value;

    /// <summary>Allows sign-in windows until the returned scope is disposed.</summary>
    public static IDisposable Allow() => new Scope(true);

    /// <summary>Forbids sign-in windows until the returned scope is disposed.</summary>
    public static IDisposable Suppress() => new Scope(false);

    private sealed class Scope : IDisposable
    {
        private readonly bool _previous;
        private bool _disposed;

        public Scope(bool allowed)
        {
            _previous = Allowed.Value;
            Allowed.Value = allowed;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Allowed.Value = _previous;
        }
    }
}
