namespace ModernWorkElevate.Core.Auth;

/// <summary>
/// Supplies a window handle that interactive WAM prompts are parented to.
/// A tray app frequently has no foreground window; the App-layer implementation
/// is responsible for always returning a usable handle — never <c>0</c>.
/// </summary>
public interface IWindowTracker
{
    /// <summary>Returns the native window handle to parent the WAM prompt on.</summary>
    nint GetCurrentWindowHandle();

    /// <summary>
    /// Called around every interactive WAM request. The tray popup hides when it loses
    /// focus, and the sign-in window takes the focus: without this the popup — the prompt's
    /// parent — would disappear under it and take the prompt with it.
    /// </summary>
    /// <returns>A scope to dispose when the prompt has closed.</returns>
    IDisposable BeginInteractivePrompt() => NoOpScope.Instance;

    private sealed class NoOpScope : IDisposable
    {
        public static readonly NoOpScope Instance = new();

        public void Dispose()
        {
        }
    }
}
