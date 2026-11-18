namespace ModernWorkElevate.AppAvalonia.Services;

using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.Extensions.Logging;
using ModernWorkElevate.Core.Updates;

/// <summary>
/// Reads this project's latest GitHub release with one anonymous GET. Nothing about the
/// user, the device or the tenants is sent — only what any HTTPS request carries (IP
/// address) and a User-Agent naming the app and its version, which GitHub's API requires.
/// </summary>
public sealed class UpdateService : IUpdateService, IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly ILogger<UpdateService> _logger;
    private readonly Version _current;

    public UpdateService(ILogger<UpdateService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        var assemblyVersion = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);
        _current = new Version(assemblyVersion.Major, assemblyVersion.Minor, Math.Max(assemblyVersion.Build, 0));

        // The system proxy with the Windows logon (SystemProxy, set in Program.Main), like
        // every other request of the app; no cookies, no credentials of its own.
        _http = new HttpClient { Timeout = Timeout };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ModernWorkElevate", _current.ToString()));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    /// <inheritdoc />
    public string CurrentVersion => _current.ToString();

    /// <inheritdoc />
    public async Task<(UpdateCheckOutcome Outcome, UpdateCheckResult? Update)> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync(GitHubReleaseParser.LatestReleaseApiUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Update check: GitHub answered HTTP {Status}", (int)response.StatusCode);
                return (UpdateCheckOutcome.Failed, null);
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (GitHubReleaseParser.Parse(json) is not { } latest)
            {
                _logger.LogWarning("Update check: the latest release could not be read");
                return (UpdateCheckOutcome.Failed, null);
            }

            if (!GitHubReleaseParser.IsNewer(latest.Version, _current))
            {
                _logger.LogInformation("Update check: {Current} is current (latest {Latest})", _current, latest.Version);
                return (UpdateCheckOutcome.UpToDate, null);
            }

            _logger.LogInformation("Update check: {Latest} is available (running {Current})", latest.Version, _current);
            return (UpdateCheckOutcome.UpdateAvailable, new UpdateCheckResult(latest.Version.ToString(3), latest.ReleasePageUrl));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Update check: GitHub could not be reached");
            return (UpdateCheckOutcome.Failed, null);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();
}
