namespace ModernWorkElevate.Tests.Updates;

using System.Text.Json;
using ModernWorkElevate.Core.Configuration;
using ModernWorkElevate.Core.Updates;

public sealed class UpdateCheckTests
{
    private const string ReleasePage = "https://github.com/mistermodernwork/modernwork-elevate/releases/tag/v1.4.0";

    private static readonly IReadOnlyDictionary<string, string?> NoTenants = new Dictionary<string, string?>();

    [Fact]
    public void Parse_LatestRelease_ReadsVersionAndReleasePage()
    {
        var release = GitHubReleaseParser.Parse(Json("v1.4.0", ReleasePage));

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 4, 0), release.Version);
        Assert.Equal(ReleasePage, release.ReleasePageUrl);
    }

    [Theory]
    [InlineData("""{ "tag_name": "v1.5.0", "html_url": "https://github.com/mistermodernwork/modernwork-elevate/releases/tag/v1.5.0", "draft": false, "prerelease": true }""")]
    [InlineData("""{ "tag_name": "v1.5.0", "html_url": "https://github.com/mistermodernwork/modernwork-elevate/releases/tag/v1.5.0", "draft": true, "prerelease": false }""")]
    public void Parse_PrereleaseOrDraft_IsNeverOffered(string json)
        => Assert.Null(GitHubReleaseParser.Parse(json));

    [Theory]
    [InlineData("release-2026")]
    [InlineData("v1.4")]
    [InlineData("")]
    public void Parse_TagThatIsNotAVersion_IsIgnored(string tag)
        => Assert.Null(GitHubReleaseParser.Parse(Json(tag, ReleasePage)));

    [Theory]
    [InlineData("https://evil.example/modernwork-elevate/releases/tag/v1.4.0")]
    [InlineData("http://github.com/mistermodernwork/modernwork-elevate/releases/tag/v1.4.0")]
    [InlineData("https://github.com/someone-else/modernwork-elevate/releases/tag/v1.4.0")]
    [InlineData("https://github.com/mistermodernwork/modernwork-elevate.evil.example/releases/tag/v1.4.0")]
    public void Parse_ReleasePageOutsideThisRepository_IsRejected(string url)
    {
        // The button opens this URL in the browser — only this project's release page qualifies.
        Assert.Null(GitHubReleaseParser.Parse(Json("v1.4.0", url)));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{ "message": "API rate limit exceeded" }""")]
    public void Parse_UnusableAnswer_ReturnsNull(string json)
        => Assert.Null(GitHubReleaseParser.Parse(json));

    [Theory]
    [InlineData("1.4.0", "1.3.6", true)]
    [InlineData("1.4.0", "1.4.0.0", false)]
    [InlineData("1.4.0", "1.4.1", false)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.10.0", "1.9.0", true)]
    public void IsNewer_ComparesMajorMinorPatch(string latest, string current, bool expected)
        => Assert.Equal(expected, GitHubReleaseParser.IsNewer(Version.Parse(latest), Version.Parse(current)));

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void MachinePolicy_UpdateCheck_IsReadAsForcedOnOrOff(int value, bool expected)
    {
        var flags = new Dictionary<string, object?> { [MachinePolicy.UpdateCheckValue] = value };

        var policy = MachinePolicy.Parse(flags, NoTenants, out _);

        Assert.Equal(expected, policy.UpdateCheck);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("maybe")]
    public void MachinePolicy_UpdateCheckMissingOrUnreadable_LeavesItToTheUser(object? value)
    {
        var flags = new Dictionary<string, object?>();
        if (value is not null)
        {
            flags[MachinePolicy.UpdateCheckValue] = value;
        }

        Assert.Null(MachinePolicy.Parse(flags, NoTenants, out _).UpdateCheck);
    }

    [Fact]
    public void ApplyMachinePolicy_CarriesTheUpdateCheckEvenWithALockedTenantList()
    {
        var options = new ModernWorkElevateOptions();

        options.ApplyMachinePolicy(new MachinePolicy { LockTenants = true, UpdateCheck = false });

        Assert.False(options.UpdateCheckByPolicy);
    }

    [Theory]
    [InlineData(null, false, false, true)]
    [InlineData(null, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, false, false)]
    public void UpdateCheckRules_PolicyWinsOverTheUser(bool? policy, bool userEnabled, bool expectedAutomatic, bool expectedManual)
    {
        Assert.Equal(expectedAutomatic, UpdateCheckRules.IsAutomaticCheckEnabled(policy, userEnabled));
        Assert.Equal(expectedManual, UpdateCheckRules.IsManualCheckAllowed(policy));
    }

    [Fact]
    public void UserSettings_UpdateCheckIsOffByDefault()
        => Assert.False(UserSettings.Default.UpdateCheckEnabled);

    [Fact]
    public void UserSettings_OldFileWithTheFormerSwitchOn_DoesNotTurnTheCheckOn()
    {
        // Earlier versions wrote "AutomaticUpdatesEnabled": true by default, without asking.
        // That is not consent to contact GitHub.
        const string oldFile = """{ "Theme": 0, "DefaultDurationHours": 1, "ExpiryWarningEnabled": true, "ExpiryWarningMinutes": 5, "AutomaticUpdatesEnabled": true }""";

        var settings = JsonSerializer.Deserialize<UserSettings>(oldFile);

        Assert.NotNull(settings);
        Assert.False(settings.UpdateCheckEnabled);
    }

    private static string Json(string tag, string url)
        => $$"""{ "tag_name": "{{tag}}", "html_url": "{{url}}", "draft": false, "prerelease": false, "assets": [] }""";
}
