namespace ModernWorkElevate.Tests.Configuration;

using System.Text;
using Microsoft.Extensions.Configuration;
using ModernWorkElevate.Core.Auth;
using ModernWorkElevate.Core.Configuration;

/// <summary>
/// The machine policy (HKLM\SOFTWARE\Policies\ModernWorkElevate): parsing the raw
/// registry data, the hardened defaults, and how the policy overrides every per-user layer.
/// </summary>
public sealed class MachinePolicyTests
{
    private const string TenantA = "7b1c4d2e-0000-4000-8000-0000000000aa";
    private const string TenantB = "7b1c4d2e-0000-4000-8000-0000000000bb";
    private const string AppA = "8f3a1c2e-0000-4000-8000-000000000001";
    private const string AppB = "8f3a1c2e-0000-4000-8000-000000000002";

    private static readonly IReadOnlyDictionary<string, object?> NoFlags = new Dictionary<string, object?>();

    [Fact]
    public void None_IsTheHardenedDefault()
    {
        var policy = MachinePolicy.None;

        Assert.False(policy.LockTenants);
        Assert.Empty(policy.Tenants);
    }

    [Fact]
    public void Options_DeviceCodeAndDiskCache_AreNeverAllowed()
    {
        var options = new ModernWorkElevateOptions();

        Assert.False(options.DeviceCodeAllowed);
        Assert.False(options.PersistentTokenCacheAllowed);
        Assert.False(options.TenantsLockedByPolicy);
    }

    [Fact]
    public void Bind_PolicyFlagsInAPerUserFile_AreIgnored()
    {
        // appsettings.local.json is writable by anything running as the user; it must not be
        // able to switch on what only an administrator may.
        const string layer = """
            { "ModernWorkElevate": { "DeviceCodeAllowed": true, "PersistentTokenCacheAllowed": true, "TenantsLockedByPolicy": true } }
            """;

        var options = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(layer)))
            .Build()
            .GetSection(ModernWorkElevateOptions.SectionName)
            .Get<ModernWorkElevateOptions>() ?? new ModernWorkElevateOptions();

        Assert.False(options.DeviceCodeAllowed);
        Assert.False(options.PersistentTokenCacheAllowed);
        Assert.False(options.TenantsLockedByPolicy);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("no", false)]
    public void Parse_LockTenantsFlag(object value, bool expected)
    {
        var flags = new Dictionary<string, object?> { [MachinePolicy.LockTenantsValue] = value };

        var policy = MachinePolicy.Parse(flags, new Dictionary<string, string?>(), out _);

        Assert.Equal(expected, policy.LockTenants);
    }

    [Fact]
    public void Apply_RegistryValuesForDeviceCodeOrTokenCache_ChangeNothing()
    {
        // Legacy switches written by hand into the policy key must not re-enable either.
        var flags = new Dictionary<string, object?> { ["AllowDeviceCode"] = 1, ["AllowPersistentTokenCache"] = 1 };
        var options = new ModernWorkElevateOptions();

        options.ApplyMachinePolicy(MachinePolicy.Parse(flags, new Dictionary<string, string?>(), out _));

        Assert.False(options.DeviceCodeAllowed);
        Assert.False(options.PersistentTokenCacheAllowed);
    }

    [Fact]
    public void Parse_TenantWithLabelAndCloud()
    {
        var tenants = new Dictionary<string, string?> { [TenantA.ToUpperInvariant()] = $"{AppA}; Contoso ;China" };

        var policy = MachinePolicy.Parse(NoFlags, tenants, out var skipped);

        var entry = Assert.Single(policy.Tenants);
        Assert.Equal((TenantA, AppA, "China", "Contoso"), (entry.TenantId, entry.ClientId, entry.Cloud, entry.Label));
        Assert.Empty(skipped);
    }

    [Theory]
    [InlineData("not-a-guid", AppA)]
    [InlineData(TenantA, "not-a-guid")]
    [InlineData(TenantA, "")]
    [InlineData(TenantA, null)]
    [InlineData(TenantA, AppA + ";Label;Mars")]
    public void Parse_InvalidTenantEntry_IsSkippedNotFatal(string name, string? data)
    {
        var tenants = new Dictionary<string, string?> { [name] = data, [TenantB] = AppB };

        var policy = MachinePolicy.Parse(NoFlags, tenants, out var skipped);

        Assert.Equal(TenantB, Assert.Single(policy.Tenants).TenantId);
        Assert.Equal(name, Assert.Single(skipped));
    }

    [Fact]
    public void Apply_UnlockedPolicy_AddsTenantsAndReplacesTheSameSlot()
    {
        var options = new ModernWorkElevateOptions
        {
            Scopes = ["User.Read"],
            TenantAppRegistrations =
            [
                new TenantAppRegistration { TenantId = TenantA, ClientId = AppB, Label = "Mine" },
                new TenantAppRegistration { TenantId = TenantB, ClientId = AppB },
            ],
        };

        options.ApplyMachinePolicy(new MachinePolicy
        {
            Tenants = [new TenantAppRegistration { TenantId = TenantA, ClientId = AppA, Label = "Org" }],
        });

        Assert.Equal(2, options.TenantAppRegistrations.Count);
        Assert.Equal(AppA, options.ClientIdFor(EntraCloud.Global, TenantA));
        Assert.Equal(AppB, options.ClientIdFor(EntraCloud.Global, TenantB));
        Assert.True(new ModernWorkElevateOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void Apply_LockedPolicy_ReplacesTheWholeList()
    {
        var options = new ModernWorkElevateOptions
        {
            TenantAppRegistrations = [new TenantAppRegistration { TenantId = TenantB, ClientId = AppB }],
        };

        options.ApplyMachinePolicy(new MachinePolicy
        {
            LockTenants = true,
            Tenants = [new TenantAppRegistration { TenantId = TenantA, ClientId = AppA }],
        });

        Assert.Equal(TenantA, Assert.Single(options.TenantAppRegistrations).TenantId);
        Assert.Null(options.ClientIdFor(EntraCloud.Global, TenantB));
        Assert.True(options.TenantsLockedByPolicy);
        Assert.False(options.DeviceCodeAllowed);
    }

    [Fact]
    public void Apply_DoesNotShareEntriesWithThePolicy()
    {
        var source = new TenantAppRegistration { TenantId = TenantA, ClientId = AppA };
        var options = new ModernWorkElevateOptions();

        options.ApplyMachinePolicy(new MachinePolicy { Tenants = [source] });
        options.TenantAppRegistrations[0].Label = "changed";

        Assert.Null(source.Label);
    }

    [Fact]
    public void ShippedAppSettings_CarryNoSharedHostedClientId()
    {
        // No third-party hosted App Registration ships with the build.
        var path = Path.Combine(AppContext.BaseDirectory, "Shipped", "appsettings.json");

        Assert.True(string.IsNullOrEmpty(ModernWorkElevateOptions.ReadShippedSharedHostedClientId(path)));
    }
}
