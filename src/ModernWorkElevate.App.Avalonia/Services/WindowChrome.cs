namespace ModernWorkElevate.AppAvalonia.Services;

using System.Runtime.InteropServices;
using Avalonia.Controls;

/// <summary>
/// Gives the app's borderless windows the native Windows 11 frame: corners rounded by the
/// desktop window manager (DWM) and its soft system shadow.
/// </summary>
/// <remarks>
/// Earlier versions drew their own 12 px rounded border with a margin inside a transparent window. On
/// Windows 11 the DWM frame (8 px) and the drawn shape never matched, and the backdrop shone
/// through the gap as a tinted rim around the panel. Now the content fills the window and
/// Windows rounds and shadows it — the same geometry as the Start menu and the quick settings.
/// Best effort: on a system without the attribute the window simply stays square.
/// </remarks>
internal static class WindowChrome
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    /// <summary>Applies the frame once the window has a native handle.</summary>
    public static void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Opened += (_, _) => Apply(window);
    }

    private static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var preference = DwmwcpRound;
            _ = DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not Windows 11 / no DWM: square corners, nothing else changes.
        }
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
