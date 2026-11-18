namespace ModernWorkElevate.Core.Updates;

/// <summary>The newest published release of this project, as GitHub reports it.</summary>
/// <param name="Version">The release version, from its <c>v1.2.3</c> tag.</param>
/// <param name="ReleasePageUrl">
/// The release's page on GitHub — always inside this project's releases, checked by
/// <see cref="GitHubReleaseParser"/>, because the update notice opens it in the browser.
/// </param>
public sealed record LatestRelease(Version Version, string ReleasePageUrl);
