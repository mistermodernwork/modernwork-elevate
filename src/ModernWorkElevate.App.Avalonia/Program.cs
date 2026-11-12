namespace ModernWorkElevate.AppAvalonia;

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModernWorkElevate.AppAvalonia.Services;
using ModernWorkElevate.Core.Auth;
using ModernWorkElevate.Core.Configuration;
using ModernWorkElevate.Core.Networking;
using ModernWorkElevate.Core.Services;

/// <summary>
/// Entry point for the Avalonia tray app. Installed per machine by an
/// MSI (no self-update). The Windows version check and the maintenance
/// switches (<c>--remove-user-data</c>, deployment arguments) run before Avalonia
/// spins up the UI thread. A second normal launch is rejected by a single-instance
/// gate so only one tray icon ever exists per user session.
/// </summary>
public static class Program
{
    /// <summary>
    /// Marks a launch as the replacement half of an in-app restart: instead of
    /// bowing out when the (still exiting) old instance holds the mutex, the new
    /// process waits for it. Passed by <c>SettingsPanelViewModel.RestartApp</c>.
    /// </summary>
    public const string RestartArgument = "--restart";

    /// <summary>
    /// Removes the calling user's data and sign-ins, then exits (see <see cref="RemoveUserData"/>).
    /// Run as the user, e.g. by <c>packaging\scripts\Remove-ModernWorkElevateUserData.ps1</c>
    /// before or after an MSI uninstall — an MSI running as SYSTEM cannot reach the users'
    /// profiles or their broker sign-ins.
    /// </summary>
    public const string RemoveUserDataArgument = "--remove-user-data";

    /// <summary>Minimum supported Windows build: Windows 11 25H2 (10.0.26200).</summary>
    public const int MinimumWindowsBuild = 26200;

    // Session-scoped (Local namespace) names: one tray instance per interactive
    // login, while a second Windows user on the same machine stays independent.
    private const string SingleInstanceMutexName = "ModernWorkElevate.SingleInstance";
    private const string ShowWindowSignalName = "ModernWorkElevate.ShowWindow";

    // Exit codes for a deployment invocation. The app is a WinExe with no console,
    // so this is the only channel back to Intune / a logon script — and the only one
    // they evaluate. Documented in docs/unattended-deployment.md; keep them in sync.
    private const int ExitInvalidArguments = 1;
    private const int ExitConfigurationNotWritable = 2;

    // Exit code when the Windows build is below MinimumWindowsBuild.
    private const int ExitUnsupportedWindows = 3;

    // Name of the marker that records the first start of the app in a profile.
    private const string InitializedMarkerName = ".initialized";

    // The broker sign-out during --remove-user-data stops after 20 seconds, so a broker
    // that does not answer cannot keep the data directory from being deleted.
    private static readonly TimeSpan UninstallSignOutBudget = TimeSpan.FromSeconds(20);

    // Held for the whole process lifetime (static so it isn't garbage-collected,
    // which would release the mutex). The OS releases it when the process exits.
    private static Mutex? _singleInstanceMutex;

    /// <summary>
    /// Auto-reset event the primary instance waits on. A second launch sets it to
    /// ask the already-running instance to surface its tray popup. Null when it
    /// couldn't be created — single-instance still holds, we just can't wake the
    /// window. Read by <see cref="App"/> to start its listener.
    /// </summary>
    public static EventWaitHandle? ShowWindowSignal { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        // The MSI enforces the same minimum (launch condition); this covers a copied
        // folder or an in-place OS rollback. Checked before anything touches the profile.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, MinimumWindowsBuild))
        {
            NativeMessageBox.ShowError(
                "ModernWork Elevate",
                $"ModernWork Elevate requires Windows 11 25H2 (build {MinimumWindowsBuild}) or later. This computer runs build {Environment.OSVersion.Version.Build}.");
            return ExitUnsupportedWindows;
        }

        // Before the first HTTP request: a PAC file may route the sign-in hosts through an
        // authenticating proxy (tenant restrictions v2), and .NET would answer it with 407.
        SystemProxy.UseWindowsLogonForProxyAuthentication();

        if (Array.IndexOf(args, RemoveUserDataArgument) >= 0)
        {
            RemoveUserData();
            return 0;
        }

        // An unattended deployment configures and exits, before anything else runs.
        // It must come before the single-instance gate: with the tray app already
        // running that gate would pop the existing window and return 0 without ever
        // writing the file.
        AppPaths.EnsureDirectories();
        var deployment = DeploymentArguments.Parse(args);
        if (deployment.IsRequested)
        {
            return ApplyDeploymentConfiguration(deployment);
        }

        // If we can't take the mutex, another instance owns it: wake its window
        // and bow out without starting a second tray icon. The one exception is
        // a restart handover, where the owner is the old instance mid-shutdown
        // — there we wait for it to die instead of exiting (else the restart
        // ends with no instance running at all).
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isPrimaryInstance);
        if (!isPrimaryInstance && !TryTakeOverAfterRestart(args))
        {
            SignalExistingInstance();
            return 0;
        }

        CreateShowWindowSignal();

        InitializeProfileOnFirstStart();

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Avalonia application builder. Public so the Avalonia previewer can find it.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// Persists the registration an unattended deployment asked for and reports the
    /// outcome as a process exit code. Never starts the UI: the caller is an install
    /// command that waits for this process to end.
    /// </summary>
    private static int ApplyDeploymentConfiguration(DeploymentArguments.Result deployment)
    {
        if (deployment.Registration is null)
        {
            return ExitInvalidArguments;
        }

        try
        {
            LocalConfigStore.SaveTenantRegistration(AppPaths.LocalConfigFile, deployment.Registration);
            if (deployment.TicketSystem is not null)
            {
                SaveTicketSystem(deployment.Registration.TenantId, deployment.TicketSystem);
            }

            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // No logger exists this early, and a WinExe has no console — the exit
            // code is all the deployment gets. JsonException means an existing
            // config file is corrupt; overwriting it would lose the user's tenants.
            return ExitConfigurationNotWritable;
        }
    }

    /// <summary>
    /// On a restart launch, waits for the old instance to exit and release the
    /// single-instance mutex (it never releases explicitly, so ownership arrives
    /// as an abandoned mutex when its process dies). Returns true once this
    /// process owns the mutex and may continue as the primary instance; false
    /// on a normal (non-restart) launch or if the old instance is still alive
    /// after the timeout.
    /// </summary>
    /// <param name="args">The process command-line arguments.</param>
    /// <returns>True when this process now owns the single-instance mutex.</returns>
    private static bool TryTakeOverAfterRestart(string[] args)
    {
        if (Array.IndexOf(args, RestartArgument) < 0)
        {
            return false;
        }

        try
        {
            // ponytail: 10 s covers any realistic shutdown; on timeout we fall
            // back to today's behavior (wake the survivor and exit).
            return _singleInstanceMutex!.WaitOne(TimeSpan.FromSeconds(10));
        }
        catch (AbandonedMutexException)
        {
            // The expected handover: the old process exited while we waited.
            // Abandonment still transfers ownership to us.
            return true;
        }
    }

    /// <summary>
    /// Creates the named auto-reset event the running instance listens on. Done
    /// here (before Avalonia starts) so the handle exists by the time any second
    /// launch tries to signal it.
    /// </summary>
    private static void CreateShowWindowSignal()
    {
        try
        {
            ShowWindowSignal = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, ShowWindowSignalName);
        }
        catch
        {
            // Non-fatal: the mutex still enforces single-instance; we just can't
            // surface the existing window when a second launch is attempted.
            ShowWindowSignal = null;
        }
    }

    /// <summary>
    /// Best-effort nudge to the already-running instance to show its tray popup.
    /// Called from a second launch right before it exits.
    /// </summary>
    private static void SignalExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowWindowSignalName, out var handle))
            {
                using (handle)
                {
                    handle.Set();
                }
            }
        }
        catch
        {
            // Best effort — the user can still open the running instance from the tray.
        }
    }

    /// <summary>
    /// Records the ticketing system <paramref name="tenantId"/> uses, so the activation
    /// form prefills it for every user in that tenant.
    /// </summary>
    /// <remarks>
    /// This lands in <c>settings.json</c>, not in the registration list — it is workflow,
    /// not auth configuration. Reuses <see cref="UserSettingsService"/> rather than
    /// hand-writing the file, which keeps the atomic temp-and-rename write and the
    /// serializer settings in one place. Blocking on the async API is confined to this
    /// deployment path, which exits immediately afterwards and never starts the UI.
    /// </remarks>
    private static void SaveTicketSystem(string tenantId, string ticketSystem)
    {
        var settings = new UserSettingsService(AppPaths.SettingsFile, NullLogger<UserSettingsService>.Instance);
        settings.LoadAsync().GetAwaiter().GetResult();

        settings.UpdateAsync(current => current.WithTicketSystem(tenantId, ticketSystem)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Removes everything the app wrote for the calling user, so a removal leaves no trace:
    /// the broker accounts' sign-ins in Windows' token broker, both per-user data directories
    /// (settings, enrolled accounts, tenant registrations, logs, an optional token cache) and
    /// the autostart entry.
    /// </summary>
    /// <remarks>
    /// Only reached through <see cref="RemoveUserDataArgument"/>, never by an update: an MSI
    /// major upgrade keeps every user's configuration and sign-ins. Every step is best effort,
    /// and one that fails must not keep the next from cleaning up what it can.
    /// </remarks>
    private static void RemoveUserData()
    {
        try
        {
            new AutostartService().Disable();
        }
        catch
        {
            // The MSI does not remove this itself — it is per user. A registry failure
            // must not abort the cleanup.
        }

        // Before the data directory goes: the sign-out reads the enrolled accounts and the
        // registrations from it.
        SignOutBrokerAccounts();

        foreach (var directory in new[] { AppPaths.DataDirectory, AppPaths.LocalDataDirectory })
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A second instance still running holds the rolling log file open. Expected;
                // the script stops running instances first.
            }
        }
    }

    /// <summary>
    /// Signs every broker account out of the app in Windows' token broker
    /// (<see cref="UninstallSignOut"/>). WAM keeps those refresh tokens outside the data
    /// directory, so deleting the directory alone leaves the accounts signed in to the app.
    /// </summary>
    /// <remarks>
    /// The registrations come from the same files as in the app
    /// (<see cref="App.AddConfigurationFiles"/>); the pre-0.7.0 migration the app also runs is
    /// skipped, as a configuration that old has not been started since. It runs on a thread-pool
    /// thread, because Main is STA without a message pump, and stops after
    /// <see cref="UninstallSignOutBudget"/>: a broker that does not answer must not cost the
    /// deletion that follows. The blocking wait is Main-time code, before any UI exists.
    /// </remarks>
    private static void SignOutBrokerAccounts()
    {
        using var budget = new CancellationTokenSource(UninstallSignOutBudget);
        try
        {
            Task.Run(async () =>
            {
                var options = App.AddConfigurationFiles(new ConfigurationBuilder())
                    .Build()
                    .GetSection(ModernWorkElevateOptions.SectionName)
                    .Get<ModernWorkElevateOptions>() ?? new ModernWorkElevateOptions();
                options.ApplyMachinePolicy(MachinePolicyReader.Read(out _));
                var wrapped = Options.Create(options);
                using var auth = new MsalAuthService(
                    wrapped,
                    new NoPromptWindowTracker(),
                    new TokenCacheFactory(wrapped, NullLogger<TokenCacheFactory>.Instance),
                    new AccountStore(AppPaths.AccountsFile, NullLogger<AccountStore>.Instance),
                    NullLogger<MsalAuthService>.Instance);
                await UninstallSignOut.SignOutBrokerAccountsAsync(auth, budget.Token).ConfigureAwait(false);
            }).Wait(UninstallSignOutBudget);
        }
        catch
        {
            // Best effort like every uninstall step: a broken configuration file or a broker
            // error must not keep the data directory from being deleted.
        }
    }

    /// <summary>
    /// First start of this build in a profile: turns "Start with Windows" on, like the
    /// earlier per-user installer did for every user. A per-machine MSI runs as SYSTEM and cannot
    /// write the users' <c>HKCU\...\Run</c>, so the app does it once itself; the user can turn
    /// it off in the tray menu, and that choice is never overridden again. Also re-points a
    /// Run entry an earlier install left behind at this executable.
    /// </summary>
    private static void InitializeProfileOnFirstStart()
    {
        var marker = Path.Combine(AppPaths.DataDirectory, InitializedMarkerName);
        if (File.Exists(marker))
        {
            return;
        }

        try
        {
            new AutostartService().Enable();
        }
        catch
        {
            // A registry write failure must not stop the app; the user can still toggle
            // autostart from the tray menu. No logger exists yet.
        }

        try
        {
            File.WriteAllText(marker, string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without the marker the next start simply enables autostart again.
        }
    }

    /// <summary>
    /// The window tracker for <see cref="RemoveUserDataArgument"/>, which has no window and must never prompt:
    /// asking for a parent window there means an interactive WAM call slipped in.
    /// </summary>
    private sealed class NoPromptWindowTracker : IWindowTracker
    {
        public nint GetCurrentWindowHandle()
            => throw new InvalidOperationException("The uninstall hook signs accounts out silently and never opens a WAM prompt.");
    }
}
