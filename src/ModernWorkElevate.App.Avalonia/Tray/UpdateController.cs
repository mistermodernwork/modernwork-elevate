namespace ModernWorkElevate.AppAvalonia.Tray;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModernWorkElevate.AppAvalonia.Services;
using ModernWorkElevate.AppAvalonia.ViewModels;
using ModernWorkElevate.AppAvalonia.Views;
using ModernWorkElevate.Core.Configuration;
using ModernWorkElevate.Core.Services;
using ModernWorkElevate.Core.Updates;

/// <summary>
/// The update check: shortly after launch and then once a day it asks GitHub for the latest
/// release — only when the machine policy or the user turned the check on — and shows a
/// small always-on-top window <b>only when a newer version exists</b>. Its button opens the
/// release page; nothing is downloaded or run. The Settings button checks on demand.
/// </summary>
public sealed class UpdateController
{
    private const int ScreenMargin = 12;

    // Logical-height estimate used to anchor the window before its first layout
    // pass populates the real Bounds; corrected immediately afterwards.
    private const double EstimatedHeight = 150;

    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(24);

    private readonly UpdatePromptWindow _window;
    private readonly UpdatePromptViewModel _viewModel;
    private readonly IUpdateService _updateService;
    private readonly IUserSettingsService _settings;
    private readonly ModernWorkElevateOptions _options;
    private readonly ILogger<UpdateController> _logger;
    private readonly DispatcherTimer _timer;

    // Versions the user dismissed this session — the daily check does not nag again
    // until restart; a deliberate "Check now" still shows them.
    private readonly HashSet<string> _dismissedVersions = new(StringComparer.Ordinal);

    private UpdateCheckResult? _pending;
    private bool _allowClose;
    private bool _busy;

    public UpdateController(
        UpdatePromptWindow window,
        UpdatePromptViewModel viewModel,
        IUpdateService updateService,
        IUserSettingsService settings,
        IOptions<ModernWorkElevateOptions> options,
        SettingsPanelViewModel settingsPanel,
        ILogger<UpdateController> logger)
    {
        ArgumentNullException.ThrowIfNull(settingsPanel);
        ArgumentNullException.ThrowIfNull(options);
        _window = window;
        _viewModel = viewModel;
        _updateService = updateService;
        _settings = settings;
        _options = options.Value;
        _logger = logger;
        _window.DataContext = _viewModel;

        // Hide instead of closing on the OS close action — the window is reused.
        _window.Closing += (_, args) =>
        {
            if (_allowClose)
            {
                return;
            }

            args.Cancel = true;
            _window.Hide();
        };

        _window.OpenReleasePageRequested += OnOpenReleasePageRequested;
        _window.LaterRequested += OnLaterRequested;

        // The Settings "Check for updates" button routes here (the VM has no direct
        // controller dependency — it raises this event and renders the outcome).
        settingsPanel.CheckForUpdatesRequested += CheckNowAsync;

        _timer = new DispatcherTimer { Interval = InitialDelay };
        _timer.Tick += OnTimerTick;
    }

    /// <summary>Allows the next window-close to actually close (called on app exit).</summary>
    public void PrepareForShutdown() => _allowClose = true;

    /// <summary>
    /// Starts the daily timer. Each tick re-reads policy and setting, so turning the check
    /// on or off takes effect without a restart; while it is off, a tick makes no request.
    /// </summary>
    public void Start() => _timer.Start();

    /// <summary>
    /// Runs a check now and reports what it found — the Settings button. A press is the
    /// user's consent for this one check, so the switch does not gate it; only a policy
    /// that forbids contacting GitHub does.
    /// </summary>
    public Task<UpdateCheckOutcome> CheckNowAsync()
        => UpdateCheckRules.IsManualCheckAllowed(_options.UpdateCheckByPolicy)
            ? CheckAsync(manual: true)
            : Task.FromResult(UpdateCheckOutcome.DisabledByPolicy);

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        // The first tick fires after InitialDelay; from then on daily.
        _timer.Interval = PollInterval;

        if (_busy || !UpdateCheckRules.IsAutomaticCheckEnabled(_options.UpdateCheckByPolicy, _settings.Current.UpdateCheckEnabled))
        {
            return;
        }

        await CheckAsync(manual: false);
    }

    private async Task<UpdateCheckOutcome> CheckAsync(bool manual)
    {
        _busy = true;
        try
        {
            var (outcome, update) = await _updateService.CheckAsync().ConfigureAwait(true);

            // Like Rufus: nothing to show unless there is something newer.
            if (update is null || (!manual && _dismissedVersions.Contains(update.Version)))
            {
                return outcome;
            }

            _pending = update;
            _viewModel.NewVersion = update.Version;
            _viewModel.CurrentVersion = _updateService.CurrentVersion;
            Show();
            return outcome;
        }
        finally
        {
            _busy = false;
        }
    }

    private void OnOpenReleasePageRequested(object? sender, EventArgs e)
    {
        if (_pending is { } update)
        {
            // Only this project's release page gets here (GitHubReleaseParser checks it),
            // and Browser.Open accepts absolute https URLs only.
            Browser.Open(update.ReleasePageUrl, "release page", _logger);
            _dismissedVersions.Add(update.Version);
        }

        Hide();
    }

    private void OnLaterRequested(object? sender, EventArgs e)
    {
        if (_pending is { } update)
        {
            _dismissedVersions.Add(update.Version);
        }

        Hide();
    }

    private void Show()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_window.IsVisible)
            {
                return;
            }

            AnchorBottomRight(EstimatedHeight);
            _window.Show();

            // Re-anchor once the real height is known (SizeToContent only fills
            // Bounds after the first layout pass).
            Dispatcher.UIThread.Post(() => AnchorBottomRight(null), DispatcherPriority.Loaded);
        });
    }

    private void Hide() => Dispatcher.UIThread.Post(() =>
    {
        if (_window.IsVisible)
        {
            _window.Hide();
        }
    });

    private void AnchorBottomRight(double? heightOverride)
    {
        var screen = _window.Screens.Primary ?? _window.Screens.All.FirstOrDefault();
        var bounds = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);

        var scale = _window.DesktopScaling;
        var logicalHeight = heightOverride
            ?? (_window.Bounds.Height > 0 ? _window.Bounds.Height : EstimatedHeight);

        var width = (int)(_window.Width * scale);
        var height = (int)(logicalHeight * scale);

        var x = bounds.Right - width - ScreenMargin;
        var y = bounds.Bottom - height - ScreenMargin;
        _window.Position = new PixelPoint(x, y);
    }
}
