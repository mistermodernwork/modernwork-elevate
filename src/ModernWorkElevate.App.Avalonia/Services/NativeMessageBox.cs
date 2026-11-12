namespace ModernWorkElevate.AppAvalonia.Services;

using System.Runtime.InteropServices;

/// <summary>
/// A plain Win32 message box for errors that occur before Avalonia is running
/// (e.g. the minimum-Windows check in <see cref="Program"/>).
/// </summary>
internal static class NativeMessageBox
{
    private const uint MbOk = 0x00000000;
    private const uint MbIconError = 0x00000010;
    private const uint MbSetForeground = 0x00010000;

    /// <summary>Shows a modal error box. Never throws.</summary>
    public static void ShowError(string title, string message)
    {
        try
        {
            _ = MessageBoxW(IntPtr.Zero, message, title, MbOk | MbIconError | MbSetForeground);
        }
        catch
        {
            // No UI available (e.g. a non-interactive session): the exit code reports it.
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
