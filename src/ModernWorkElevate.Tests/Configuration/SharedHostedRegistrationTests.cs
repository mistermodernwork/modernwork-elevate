namespace ModernWorkElevate.Tests.Configuration;

using System.Text;
using Microsoft.Extensions.Configuration;
using ModernWorkElevate.Core.Auth;
using ModernWorkElevate.Core.Configuration;

/// <summary>
/// The rules behind the shared-hosted way: which tenant counts as shared-hosted, which client id
/// an entry gets, the admin-consent link, and where the shared client id may come from. A wrong
/// answer here labels another app "shared-hosted", hands an admin a consent link for it, or writes
/// an entry the next start rejects.
/// </summary>
public sealed class SharedHostedRegistrationTests : IDisposable
{
    private const string SharedAppId = "5d0c1f2e-0000-4000-8000-0000000000f1";
    private const string OwnAppId = "8f3a1c2e-0000-4000-8000-000000000001";
    private const string TenantA = "7b1c4d2e-0000-4000-8000-0000000000aa";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "ModernWork Elevate-SharedHosted-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(SharedAppId, EntraCloud.Global, AppRegistrationHosting.SharedHosted)]
    [InlineData("{5D0C1F2E-0000-4000-8000-0000000000F1}", EntraCloud.Global, AppRegistrationHosting.SharedHosted)]
    [InlineData(SharedAppId, EntraCloud.China, AppRegistrationHosting.SelfHosted)]
    [InlineData(OwnAppId, EntraCloud.Global, AppRegistrationHosting.SelfHosted)]
    [InlineData(null, EntraCloud.Global, AppRegistrationHosting.SelfHosted)]
    public void HostingOf_IsSharedHostedOnlyForTheSharedIdInTheGlobalCloud(string? clientId, EntraCloud cloud, AppRegistrationHosting expected)
        => Assert.Equal(expected, Shared().HostingOf(cloud, clientId));

    [Fact]
    public void EnrollmentScopes_SharedHostedAsksForGraphDefault_SelfHostedForTheExplicitList()
    {
        // .default makes a first consent list the Azure permission too (field test 2026-10-20).
        // Self-hosted must never get it: a customer's registration may declare more than the
        // app needs, and every granted Graph scope would land in every token.
        var options = Shared();
        options.Scopes = ["User.Read", "Group.Read.All"];

        Assert.Equal(["https://graph.microsoft.com/.default"], options.EnrollmentScopes(EntraCloud.Global, SharedAppId));
        Assert.Equal(options.Scopes, options.EnrollmentScopes(EntraCloud.Global, OwnAppId));
        Assert.Equal(options.Scopes, options.EnrollmentScopes(EntraCloud.China, SharedAppId));
        Assert.Equal(options.Scopes, new ModernWorkElevateOptions { Scopes = options.Scopes }.EnrollmentScopes(EntraCloud.Global, SharedAppId));
    }

    [Fact]
    public void HostingOf_WithoutAShippedSharedId_IsAlwaysSelfHosted()
    {
        // An empty SharedHostedClientId is how a build ships without the feature.
        var options = new ModernWorkElevateOptions();

        Assert.Equal(AppRegistrationHosting.SelfHosted, options.HostingOf(EntraCloud.Global, SharedAppId));
        Assert.Equal(AppRegistrationHosting.SelfHosted, options.HostingOf(EntraCloud.Global, string.Empty));
    }

    [Fact]
    public void HostingFor_FollowsTheTenantsConfiguredEntry()
    {
        var options = Shared();
        options.TenantAppRegistrations.Add(new TenantAppRegistration { TenantId = TenantA, ClientId = SharedAppId, Cloud = "Global" });

        Assert.Equal(AppRegistrationHosting.SharedHosted, options.HostingFor(EntraCloud.Global, TenantA));
        Assert.Equal(AppRegistrationHosting.SelfHosted, options.HostingFor(EntraCloud.Global, Guid.NewGuid().ToString()));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData(null)]
    public void UseSharedHostedClientId_AnythingButAGuid_CountsAsNone(string? value)
    {
        // A shared-hosted entry copies this value into ClientId; the validator would reject a
        // non-GUID there at the next start and close the app without a window.
        var options = new ModernWorkElevateOptions();

        options.UseSharedHostedClientId(value);

        Assert.Null(options.SharedHostedClientId);
    }

    [Fact]
    public void UseSharedHostedClientId_StoresTheCanonicalForm()
    {
        var options = new ModernWorkElevateOptions();

        options.UseSharedHostedClientId("{5D0C1F2E-0000-4000-8000-0000000000F1}");

        Assert.Equal(SharedAppId, options.SharedHostedClientId);
    }

    [Theory]
    [InlineData(AppRegistrationHosting.SharedHosted, EntraCloud.Global, "ignored", SharedAppId)]
    [InlineData(AppRegistrationHosting.SharedHosted, EntraCloud.China, OwnAppId, null)]
    [InlineData(AppRegistrationHosting.SelfHosted, EntraCloud.Global, "  " + OwnAppId + " ", OwnAppId)]
    [InlineData(AppRegistrationHosting.SelfHosted, EntraCloud.China, OwnAppId, OwnAppId)]
    [InlineData(AppRegistrationHosting.SelfHosted, EntraCloud.Global, "", null)]
    [InlineData(AppRegistrationHosting.SelfHosted, EntraCloud.Global, "not-a-guid", null)]
    public void ResolveClientId_FollowsTheChosenWay(AppRegistrationHosting hosting, EntraCloud cloud, string typed, string? expected)
        => Assert.Equal(expected, Shared().ResolveClientId(hosting, cloud, typed));

    [Fact]
    public void ResolveClientId_SharedHostedWithoutAShippedId_IsInvalid()
        => Assert.Null(new ModernWorkElevateOptions().ResolveClientId(AppRegistrationHosting.SharedHosted, EntraCloud.Global, null));

    [Fact]
    public void SharedAdminConsentUrl_IsTheDocumentedLinkReturningToTheConsentPage()
    {
        // The redirect URI must match the one registered on the shared app character for
        // character, or Entra refuses the link (AADSTS50011).
        var url = Shared().SharedAdminConsentUrl(EntraCloud.Global, TenantA.ToUpperInvariant());

        Assert.Equal(
            $"https://login.microsoftonline.com/{TenantA}/adminconsent?client_id={SharedAppId}"
            + "&redirect_uri=https%3A%2F%2Flogin.microsoftonline.com%2Fcommon%2Foauth2%2Fnativeclient",
            url);
    }

    [Fact]
    public void SharedAdminConsentUrl_OutsideTheGlobalCloudOrWithoutASharedId_IsNull()
    {
        Assert.Null(Shared().SharedAdminConsentUrl(EntraCloud.China, TenantA));
        Assert.Null(Shared().SharedAdminConsentUrl(EntraCloud.Global, "not-a-guid"));
        Assert.Null(new ModernWorkElevateOptions().SharedAdminConsentUrl(EntraCloud.Global, TenantA));
    }

    [Fact]
    public void Bind_ASharedIdInAnyConfigurationLayer_IsIgnored()
    {
        // The per-user appsettings.local.json is writable by anything running as the user.
        // It must not be able to point "shared-hosted", and the consent link, at another app.
        const string layer = """
            { "ModernWorkElevate": { "Scopes": [ "User.Read" ], "SharedHostedClientId": "8f3a1c2e-0000-4000-8000-000000000001" } }
            """;

        var options = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(layer)))
            .Build()
            .GetSection(ModernWorkElevateOptions.SectionName)
            .Get<ModernWorkElevateOptions>()!;

        Assert.Null(options.SharedHostedClientId);
    }

    [Fact]
    public void ReadShippedSharedHostedClientId_ReadsTheKey()
    {
        var path = WriteFile("""{ "ModernWorkElevate": { "SharedHostedClientId": "5d0c1f2e-0000-4000-8000-0000000000f1" } }""");

        Assert.Equal(SharedAppId, ModernWorkElevateOptions.ReadShippedSharedHostedClientId(path));
    }

    [Theory]
    [InlineData("""{ "ModernWorkElevate": { "SharedHostedClientId": 42 } }""")]
    [InlineData("""{ "ModernWorkElevate": { } }""")]
    [InlineData("{ not json")]
    public void ReadShippedSharedHostedClientId_BrokenOrMissingValue_IsNullNotAnError(string content)
        => Assert.Null(ModernWorkElevateOptions.ReadShippedSharedHostedClientId(WriteFile(content)));

    [Fact]
    public void ReadShippedSharedHostedClientId_MissingFile_IsNull()
        => Assert.Null(ModernWorkElevateOptions.ReadShippedSharedHostedClientId(Path.Combine(_directory, "missing.json")));

    [Fact]
    public void CommittedAppSettings_SharedIdIsEmptyOrAGuidAndTheFileValidates()
    {
        // The real file the app ships, linked into the test output. Until the release gate in
        // docs/adr/0002 is met the id stays empty; once set it must be a GUID, or shared-hosted
        // silently disappears from the build.
        var path = Path.Combine(AppContext.BaseDirectory, "Shipped", "appsettings.json");
        var shipped = ModernWorkElevateOptions.ReadShippedSharedHostedClientId(path);
        Assert.True(string.IsNullOrEmpty(shipped) || Guid.TryParse(shipped, out _), $"SharedHostedClientId '{shipped}' is neither empty nor a GUID.");

        // The configuration system tolerates comments and trailing commas; this reader and the
        // install script's ConvertFrom-Json do not. If they disagree, shared-hosted would vanish
        // from the build without a word.
        var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();
        Assert.Equal(configuration["ModernWorkElevate:SharedHostedClientId"] ?? string.Empty, shipped ?? string.Empty);

        var options = configuration.GetSection(ModernWorkElevateOptions.SectionName).Get<ModernWorkElevateOptions>()!;
        options.UseSharedHostedClientId(shipped);

        Assert.True(new ModernWorkElevateOptionsValidator().Validate(null, options).Succeeded);
        Assert.Empty(options.TenantAppRegistrations);
    }

    [Fact]
    public void CommittedAppSettings_ASelfHostedEntryLayeredOnTop_BindsUnchanged()
    {
        // The per-user file sits on top of the shipped one; the new key must not disturb it.
        const string perUser = """
            { "ModernWorkElevate": { "TenantAppRegistrations": [ { "TenantId": "7b1c4d2e-0000-4000-8000-0000000000aa", "ClientId": "8f3a1c2e-0000-4000-8000-000000000001", "Label": "Contoso" } ] } }
            """;

        var options = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "Shipped", "appsettings.json"))
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(perUser)))
            .Build()
            .GetSection(ModernWorkElevateOptions.SectionName)
            .Get<ModernWorkElevateOptions>()!;

        var entry = Assert.Single(options.TenantAppRegistrations);
        Assert.Equal((TenantA, OwnAppId, "Global", "Contoso"), (entry.TenantId, entry.ClientId, entry.Cloud, entry.Label));
        Assert.Equal(AppRegistrationHosting.SelfHosted, options.HostingFor(EntraCloud.Global, TenantA));
    }

    private static ModernWorkElevateOptions Shared()
    {
        var options = new ModernWorkElevateOptions();
        options.UseSharedHostedClientId(SharedAppId);
        return options;
    }

    private string WriteFile(string content)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, Guid.NewGuid() + ".json");
        File.WriteAllText(path, content);
        return path;
    }
}
