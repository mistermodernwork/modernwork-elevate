namespace ModernWorkElevate.Tests.Configuration;

using ModernWorkElevate.Core.Auth;
using ModernWorkElevate.Core.Configuration;

/// <summary>
/// The green "Verified" check on a tenant card. It is only honest if one sign-in proves only
/// the registration it went through: every shared-hosted tenant shares one client id but
/// consents on its own, so a per-client-id record would turn all of them green at once.
/// </summary>
public sealed class UserSettingsVerifiedTests
{
    private const string SharedAppId = "5d0c1f2e-0000-4000-8000-0000000000f1";
    private const string OwnAppId = "8f3a1c2e-0000-4000-8000-000000000001";
    private const string TenantA = "7b1c4d2e-0000-4000-8000-0000000000aa";
    private const string TenantB = "7b1c4d2e-0000-4000-8000-0000000000bb";

    [Fact]
    public void SharedHosted_OneTenantVerified_DoesNotVerifyAnother()
    {
        var settings = UserSettings.Default.WithRegistrationVerified(EntraCloud.Global, TenantA, SharedAppId, AppRegistrationHosting.SharedHosted);

        Assert.True(settings.IsRegistrationVerified(EntraCloud.Global, TenantA.ToUpperInvariant(), SharedAppId, AppRegistrationHosting.SharedHosted));
        Assert.False(settings.IsRegistrationVerified(EntraCloud.Global, TenantB, SharedAppId, AppRegistrationHosting.SharedHosted));
        Assert.Null(settings.VerifiedClientIds);
    }

    [Fact]
    public void SelfHosted_KeepsTheExistingPerClientIdRecord()
    {
        // Unchanged from before shared-hosted: no migration, older versions read the same set.
        var settings = UserSettings.Default with { VerifiedClientIds = [OwnAppId] };

        Assert.True(settings.IsRegistrationVerified(EntraCloud.Global, TenantA, OwnAppId, AppRegistrationHosting.SelfHosted));
        Assert.True(settings.IsRegistrationVerified(EntraCloud.Global, TenantB, OwnAppId, AppRegistrationHosting.SelfHosted));
        Assert.Null(settings.VerifiedSharedHostedRegistrations);
    }

    [Fact]
    public void ATenantSwitchedToSharedHosted_StartsUnverified()
    {
        // Its own registration worked, which proves nothing about the shared registration's consent in that tenant.
        var settings = UserSettings.Default.WithRegistrationVerified(EntraCloud.Global, TenantA, OwnAppId, AppRegistrationHosting.SelfHosted);

        Assert.False(settings.IsRegistrationVerified(EntraCloud.Global, TenantA, SharedAppId, AppRegistrationHosting.SharedHosted));
    }

    [Fact]
    public void WithRegistrationVerified_AlreadyVerified_ReturnsTheSameInstance()
    {
        // Same instance means the caller can skip rewriting the settings file.
        var once = UserSettings.Default.WithRegistrationVerified(EntraCloud.Global, TenantA, SharedAppId, AppRegistrationHosting.SharedHosted);

        Assert.Same(once, once.WithRegistrationVerified(EntraCloud.Global, TenantA, SharedAppId, AppRegistrationHosting.SharedHosted));
    }
}
