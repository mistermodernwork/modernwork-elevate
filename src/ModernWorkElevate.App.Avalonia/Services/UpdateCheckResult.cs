namespace ModernWorkElevate.AppAvalonia.Services;

/// <summary>A newer release found on GitHub.</summary>
/// <param name="Version">The release version, e.g. <c>"1.4.0"</c>.</param>
/// <param name="ReleasePageUrl">Its page on GitHub, already checked to be this project's.</param>
public sealed record UpdateCheckResult(string Version, string ReleasePageUrl);
