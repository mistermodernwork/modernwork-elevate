namespace ModernWorkElevate.Core.Configuration;

using System.IO;

/// <summary>
/// Single source of truth for the per-user file locations ModernWork Elevate reads
/// and writes — keeping the paths here prevents the same string being re-typed
/// across the host wiring, the token cache, and the Settings UI.
/// <para/>
/// Layout (per-machine install in Program Files, data per user):
/// <list type="bullet">
/// <item><see cref="DataDirectory"/> = <c>%APPDATA%\ModernWorkElevate</c> — settings, tenants,
/// account metadata, favorites. No secrets: no tokens, no passwords.</item>
/// <item><see cref="LocalDataDirectory"/> = <c>%LOCALAPPDATA%\ModernWorkElevate</c> — logs and,
/// only when a machine policy allows it, the DPAPI token cache. Never roams.</item>
/// </list>
/// Both directories are created by <see cref="EnsureDirectories"/> with an ACL that grants
/// access to the current user and SYSTEM only (<see cref="UserOnlyDirectoryAcl"/>).
/// </summary>
public static class AppPaths
{
    private const string AppFolderName = "ModernWorkElevate";

    /// <summary>Root data directory: <c>%APPDATA%\ModernWorkElevate</c>.</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppFolderName);

    /// <summary>Non-roaming directory: <c>%LOCALAPPDATA%\ModernWorkElevate</c> (logs, optional token cache).</summary>
    public static string LocalDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    /// <summary>Folder holding the rolling Serilog files.</summary>
    public static string LogDirectory { get; } = Path.Combine(LocalDataDirectory, "logs");

    /// <summary>Enrolled-accounts metadata file.</summary>
    public static string AccountsFile { get; } = Path.Combine(DataDirectory, "accounts.json");

    /// <summary>Persisted justification favorites file.</summary>
    public static string FavoritesFile { get; } = Path.Combine(DataDirectory, "favorites.json");

    /// <summary>
    /// Persisted sets of Azure activation scopes. Deliberately not part of
    /// <see cref="SettingsFile"/>: that one falls back to defaults as a whole when it
    /// cannot be read, and a favourite must not be able to cost the user their theme,
    /// pins and tenant settings.
    /// </summary>
    public static string ScopeFavoritesFile { get; } = Path.Combine(DataDirectory, "scope-favorites.json");

    /// <summary>Persisted user settings file.</summary>
    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");

    /// <summary>User-entered configuration (e.g. ClientId) written by the Settings UI.</summary>
    public static string LocalConfigFile { get; } = Path.Combine(DataDirectory, "appsettings.local.json");

    /// <summary>
    /// Optional organization logo, shown at the top right of the panel. A rollout places it
    /// (install script <c>-LogoPath</c>) or the user picks one in Settings; no file, no logo.
    /// </summary>
    public static string LogoFile { get; } = Path.Combine(DataDirectory, "logo.png");

    /// <summary>
    /// Sentinel that asks for the one-time first-run setup dialog (autostart / Start
    /// menu opt-out). Earlier per-user installers wrote it; the MSI
    /// build never writes it, so the dialog does not appear. The dialog deletes it once the choice is applied, so
    /// it never reappears on subsequent starts. A leading dot keeps it visually
    /// grouped apart from the JSON config files in the data directory.
    /// </summary>
    public static string FirstRunSetupMarkerFile { get; } = Path.Combine(DataDirectory, ".setup-pending");

    /// <summary>
    /// Creates <see cref="DataDirectory"/>, <see cref="LocalDataDirectory"/> and the log folder
    /// and restricts each to the current user and SYSTEM. Called first thing in <c>Main</c>, so
    /// every file the app writes afterwards inherits that ACL. Never throws: a profile on a
    /// file system without ACL support still gets a working app.
    /// </summary>
    /// <returns>Directories whose ACL could not be applied (empty when all succeeded).</returns>
    public static IReadOnlyList<string> EnsureDirectories()
    {
        var failed = new List<string>();
        foreach (var directory in new[] { DataDirectory, LocalDataDirectory, LogDirectory })
        {
            try
            {
                Directory.CreateDirectory(directory);
                if (!UserOnlyDirectoryAcl.TryApply(directory))
                {
                    failed.Add(directory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(directory);
            }
        }

        return failed;
    }
}
