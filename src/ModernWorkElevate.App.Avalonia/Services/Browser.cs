namespace ModernWorkElevate.AppAvalonia.Services;

using System.Diagnostics;
using Microsoft.Extensions.Logging;

/// <summary>
/// Hands a URL to the user's default browser. Shared so the app's links cannot drift apart in
/// how they fail — none is worth an error in the UI, so a failure is only logged.
/// </summary>
internal static class Browser
{
    public static void Open(string url, string target, ILogger logger)
    {
        // ShellExecute runs whatever it is handed — a file: or UNC path would start a program.
        // Only absolute https links ever reach the browser.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            logger.LogWarning("Refused to open the {Target}: not an https URL", target);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to open the {Target}", target);
        }
    }
}
