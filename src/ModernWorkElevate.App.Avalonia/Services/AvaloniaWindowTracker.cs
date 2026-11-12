namespace ModernWorkElevate.AppAvalonia.Services;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using ModernWorkElevate.Core.Auth;

/// <summary>
/// Supplies a parent window handle for interactive WAM prompts and keeps the tray popup
/// open while one is showing. Uses <see cref="TopLevel.TryGetPlatformHandle"/> on the
/// registered popup when it is visible, else the foreground window — never
/// <c>IntPtr.Zero</c>.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class AvaloniaWindowTracker : IWindowTracker
{
    private Window? _trackedWindow;
    private int _openPrompts;

    /// <summary>True while at least one WAM sign-in window is open.</summary>
    public bool IsInteractivePromptOpen => Volatile.Read(ref _openPrompts) > 0;

    /// <summary>Registers <paramref name="window"/> as the WAM prompt parent.</summary>
    public void Track(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _trackedWindow = window;
    }

    /// <inheritdoc />
    public nint GetCurrentWindowHandle()
    {
        // Only a visible popup is a usable parent. A hidden one (a Refresh from the tray
        // menu, Re-activate from an expiry alert) would hide the sign-in window with it,
        // and the user would see nothing but a stalled app. IsWindowVisible is a plain
        // Win32 call, safe on the thread-pool thread MSAL calls this from.
        if (_trackedWindow is not null)
        {
            var handle = _trackedWindow.TryGetPlatformHandle()?.Handle ?? nint.Zero;
            if (handle != nint.Zero && IsWindowVisible(handle))
            {
                return handle;
            }
        }

        var foreground = GetForegroundWindow();
        return foreground != nint.Zero ? foreground : nint.Zero;
    }

    /// <inheritdoc />
    public IDisposable BeginInteractivePrompt()
    {
        Interlocked.Increment(ref _openPrompts);
        return new PromptScope(this);
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    private void EndInteractivePrompt()
    {
        if (Interlocked.Decrement(ref _openPrompts) > 0 || _trackedWindow is not { } window)
        {
            return;
        }

        // The sign-in window had the focus; hand it back so the popup's own
        // focus-loss auto-hide works again the next time the user clicks elsewhere.
        Dispatcher.UIThread.Post(() =>
        {
            if (window.IsVisible)
            {
                window.Activate();
            }
        });
    }

    private sealed class PromptScope : IDisposable
    {
        private AvaloniaWindowTracker? _owner;

        public PromptScope(AvaloniaWindowTracker owner) => _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndInteractivePrompt();
    }
}
