namespace ModernWorkElevate.Core.Updates;

using System.Text.Json;

/// <summary>
/// Reads GitHub's "latest release" answer for this project and decides whether it is
/// newer than the running version. Only checks — the app never downloads or runs anything.
/// </summary>
public static class GitHubReleaseParser
{
    /// <summary>GitHub API URL of this project's newest published (non-draft, non-prerelease) release.</summary>
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/mistermodernwork/modernwork-elevate/releases/latest";

    /// <summary>The only place a release page may point to; anything else is not opened.</summary>
    public const string ReleasePagePrefix = "https://github.com/mistermodernwork/modernwork-elevate/releases/";

    /// <summary>
    /// The release in <paramref name="json"/>, or <c>null</c> when the answer is unusable, a
    /// draft or prerelease, carries a tag that is not <c>v</c>+version, or links outside this
    /// project's releases. Never throws.
    /// </summary>
    public static LatestRelease? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || IsTrue(root, "draft")
                || IsTrue(root, "prerelease")
                || Text(root, "tag_name") is not { } tag
                || Text(root, "html_url") is not { } url)
            {
                return null;
            }

            var version = ParseTag(tag);
            return version is not null && IsThisProjectsReleasePage(url)
                ? new LatestRelease(version, url)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// True when <paramref name="latest"/> is a higher major.minor.patch than
    /// <paramref name="current"/>. The fourth part is ignored: the assembly reports
    /// <c>1.4.0.0</c> for the release tagged <c>v1.4.0</c>.
    /// </summary>
    public static bool IsNewer(Version latest, Version current)
    {
        ArgumentNullException.ThrowIfNull(latest);
        ArgumentNullException.ThrowIfNull(current);
        return Normalize(latest) > Normalize(current);
    }

    private static Version Normalize(Version version)
        => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static Version? ParseTag(string tag)
    {
        // Exactly vMAJOR.MINOR.PATCH, the form the release workflow checks the tag against.
        var text = tag.StartsWith('v') ? tag[1..] : tag;
        return text.Split('.').Length == 3 && Version.TryParse(text, out var version) ? version : null;
    }

    private static bool IsThisProjectsReleasePage(string url)
        => url.StartsWith(ReleasePagePrefix, StringComparison.Ordinal)
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host == "github.com";

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IsTrue(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
