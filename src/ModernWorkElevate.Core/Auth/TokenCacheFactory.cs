namespace ModernWorkElevate.Core.Auth;

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using ModernWorkElevate.Core.Configuration;

/// <summary>
/// Creates and registers MSAL token caches — in this app only when a machine
/// policy allows it (<see cref="ModernWorkElevateOptions.PersistentTokenCacheAllowed"/>).
/// By default nothing is written to disk: the WAM broker owns the refresh tokens and
/// MSAL keeps access tokens in its in-memory cache for the lifetime of the process.
/// When allowed, each cache file is DPAPI-encrypted and scoped to the current user under
/// <see cref="AppPaths.LocalDataDirectory"/> (never roaming). A corrupted cache is rebuilt rather than blocking startup.
/// </summary>
/// <remarks>
/// Excluded from coverage: a thin wrapper around <c>MsalCacheHelper</c> and DPAPI
/// file storage that can only be verified meaningfully against the real OS.
/// </remarks>
[ExcludeFromCodeCoverage]
public sealed class TokenCacheFactory
{
    private readonly ILogger<TokenCacheFactory> _logger;
    private bool _stalePurged;

    public TokenCacheFactory(IOptions<ModernWorkElevateOptions> options, ILogger<TokenCacheFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger;
        Enabled = options.Value.PersistentTokenCacheAllowed;
        CacheDirectory = AppPaths.LocalDataDirectory;
    }

    /// <summary>Directory holding the encrypted token cache files.</summary>
    public string CacheDirectory { get; }

    /// <summary>True when a machine policy allows token caches on disk.</summary>
    public bool Enabled { get; }

    /// <summary>
    /// Attaches a DPAPI-encrypted persistent cache stored as <paramref name="cacheFileName"/>
    /// to <paramref name="tokenCache"/>, or — the default — nothing, returning <c>null</c>:
    /// the cache then stays in memory. In that case cache files an earlier configuration or
    /// an earlier version left behind are deleted once per process, so turning the policy off
    /// also removes what it allowed.
    /// </summary>
    /// <remarks>
    /// Each PCA — one per App Registration — needs its own cache file so accounts
    /// and tokens of different registrations and STS authorities don't collide in
    /// one binary blob: <c>msal-{clientId}.cache</c> for the broker PCA and
    /// <c>msal-devicecode-{clientId}.cache</c> for the broker-less one.
    /// </remarks>
    public async Task<MsalCacheHelper?> RegisterAsync(
        ITokenCache tokenCache,
        string cacheFileName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tokenCache);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheFileName);
        ct.ThrowIfCancellationRequested();

        if (!Enabled)
        {
            PurgeStaleCacheFiles();
            return null;
        }

        Directory.CreateDirectory(CacheDirectory);

        var storageProperties = new StorageCreationPropertiesBuilder(cacheFileName, CacheDirectory).Build();

        MsalCacheHelper helper;
        try
        {
            helper = await MsalCacheHelper.CreateAsync(storageProperties).ConfigureAwait(false);
            helper.VerifyPersistence();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "MSAL token cache {File} could not be opened; rebuilding it",
                cacheFileName);
            DeleteCacheFile(cacheFileName);
            helper = await MsalCacheHelper.CreateAsync(storageProperties).ConfigureAwait(false);
        }

        helper.RegisterCache(tokenCache);
        return helper;
    }

    /// <summary>Deletes a named encrypted cache file if it exists.</summary>
    public void DeleteCacheFile(string cacheFileName)
    {
        var path = Path.Combine(CacheDirectory, cacheFileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Deletes every <c>msal*.cache</c> file (and its MSAL lock file) in <see cref="CacheDirectory"/>. Best effort:
    /// a locked file is logged and left for the next start.
    /// </summary>
    private void PurgeStaleCacheFiles()
    {
        if (_stalePurged)
        {
            return;
        }

        _stalePurged = true;
        if (!Directory.Exists(CacheDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(CacheDirectory, "msal*.cache*"))
        {
            try
            {
                File.Delete(file);
                _logger.LogInformation("Deleted token cache file {File}: persistent token caches are disabled", Path.GetFileName(file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not delete token cache file {File}", Path.GetFileName(file));
            }
        }
    }
}
