using AppForge.Core.Domain;
using AppForge.Core.Profiles;
using AppForge.Core.Versions;

namespace AppForge.Tests.Core;

public class SoftwareVersionTests
{
    [Theory]
    [InlineData("1.2.10", "1.2.9", 1)]
    [InlineData("24.08", "24.8", 0)]
    [InlineData("1.0", "1.0.0.0", 0)]
    [InlineData("v2.0", "1.99", 1)]
    [InlineData("2.0-beta", "2.0", -1)]
    [InlineData("2.0-beta", "2.0-alpha", 1)]
    [InlineData("10.0.19045", "10.0.22631", -1)]
    public void Compares_numerically_with_prerelease_suffixes(string a, string b, int expected)
    {
        Assert.Equal(expected, Math.Sign(SoftwareVersion.Parse(a).CompareTo(SoftwareVersion.Parse(b))));
    }

    [Fact]
    public void Equal_versions_have_equal_hash_codes()
    {
        Assert.Equal(SoftwareVersion.Parse("1.0").GetHashCode(), SoftwareVersion.Parse("1.0.0").GetHashCode());
    }

    private static readonly string[] Available = ["24.07", "24.08", "23.01", "24.09-beta", "22.00", "not-a-version"];

    [Fact]
    public void Latest_ignores_prereleases_and_garbage()
    {
        var r = VersionResolver.Resolve(Available, VersionMode.Latest, []);
        Assert.Equal("24.08", Assert.Single(r).Version);
    }

    [Fact]
    public void Latest_can_include_prereleases()
    {
        Assert.Equal("24.09-beta", VersionResolver.Resolve(Available, VersionMode.Latest, [], includePrerelease: true)[0].Version);
    }

    [Fact]
    public void Pinned_and_missing_pin()
    {
        Assert.Equal("23.01", VersionResolver.Resolve(Available, VersionMode.Pinned, ["23.01"])[0].Version);
        Assert.Empty(VersionResolver.Resolve(Available, VersionMode.Pinned, ["99.0"]));
    }

    [Fact]
    public void Multiple_supports_rules()
    {
        var r = VersionResolver.Resolve(Available, VersionMode.Multiple, ["latest", "latest-1", "23.*", "latest"]);
        Assert.Equal(["24.08", "24.07", "23.01"], r.Select(v => v.Version));
    }

    [Fact]
    public void Ring_mode_assigns_versions_to_rings()
    {
        var r = VersionResolver.Resolve(Available, VersionMode.Ring, [], new Dictionary<DeploymentRing, int> { [DeploymentRing.Pilot] = 0, [DeploymentRing.Production] = 1 });
        Assert.Equal(("24.08", (DeploymentRing?)DeploymentRing.Pilot), (r[0].Version, r[0].Ring));
        Assert.Equal(("24.07", (DeploymentRing?)DeploymentRing.Production), (r[1].Version, r[1].Ring));
    }

    [Fact]
    public void Empty_source_resolves_to_nothing()
    {
        Assert.Empty(VersionResolver.Resolve([], VersionMode.Latest, []));
    }
}

public class ProfileSerializerTests
{
    private static SoftwareProfile Sample()
    {
        var p = new SoftwareProfile();
        p.Metadata.Vendor = "Igor Pavlov";
        p.Metadata.Product = "7-Zip";
        p.Metadata.Owner = "team-clients";
        p.Source.Provider = SourceProviderType.Winget;
        p.Source.Id = "7zip.7zip";
        p.Phases[DeploymentPhase.PostInstall] =
        [
            new BlockInstance { Block = "registry.set-value", Parameters = { ["Key"] = @"HKLM\SOFTWARE\Contoso", ["Name"] = "Managed", ["Value"] = "1", ["Type"] = "DWord" } },
        ];
        return p;
    }

    [Fact]
    public void Yaml_roundtrip_preserves_profile()
    {
        var yaml = ProfileSerializer.ToYaml(Sample());
        var back = ProfileSerializer.FromYaml(yaml);
        Assert.Equal("7-Zip", back.Metadata.Product);
        Assert.Equal(SourceProviderType.Winget, back.Source.Provider);
        var block = Assert.Single(back.Phases[DeploymentPhase.PostInstall]);
        Assert.Equal("DWord", block.Parameters["Type"]);
        Assert.Equal("1", block.Parameters["Value"]);
    }

    [Fact]
    public void Json_roundtrip_uses_string_enums()
    {
        var json = ProfileSerializer.ToJson(Sample());
        Assert.Contains("\"Winget\"", json, StringComparison.Ordinal);
        Assert.Equal("7zip.7zip", ProfileSerializer.FromJson(json).Source.Id);
    }

    [Fact]
    public void Resolve_layers_global_template_profile()
    {
        const string global = "psadt:\n  deferTimes: 5\n  allowDefer: true\nmetadata:\n  language: DEU\n";
        const string template = "psadt:\n  deferTimes: 2\ninstaller:\n  type: Msi\n  silentArguments: /qn\n";
        const string profile = "metadata:\n  vendor: Contoso\n  product: App\ninstaller:\n  silentArguments: /qn REBOOT=ReallySuppress\n";

        var resolved = ProfileSerializer.Resolve(global, template, profile);

        Assert.Equal(2, resolved.Psadt.DeferTimes);
        Assert.True(resolved.Psadt.AllowDefer);
        Assert.Equal("DEU", resolved.Metadata.Language);
        Assert.Equal("Contoso", resolved.Metadata.Vendor);
        Assert.Equal(InstallerType.Msi, resolved.Installer.Type);
        Assert.Equal("/qn REBOOT=ReallySuppress", resolved.Installer.SilentArguments);
        Assert.Equal("4.1.7", resolved.Psadt.ToolkitVersion);
    }

    [Fact]
    public void Diff_contains_only_deviations()
    {
        var basis = new SoftwareProfile();
        var p = new SoftwareProfile();
        p.Psadt.DeferTimes = 9;
        var diff = ProfileSerializer.Diff(basis, p);
        Assert.Contains("deferTimes: 9", diff, StringComparison.Ordinal);
        Assert.DoesNotContain("toolkitVersion", diff, StringComparison.Ordinal);
        var roundtrip = ProfileSerializer.Resolve(diff);
        Assert.Equal(9, roundtrip.Psadt.DeferTimes);
    }

    [Fact]
    public void Lint_reports_missing_fields_and_mode_errors()
    {
        var p = new SoftwareProfile();
        p.Source.VersionMode = VersionMode.Pinned;
        var findings = new ProfileLinter().Lint(p);
        Assert.Contains(findings, f => f.Path == "metadata.vendor" && f.Severity == LintSeverity.Error);
        Assert.Contains(findings, f => f.Path == "source.versions");
        Assert.Contains(findings, f => f.Path == "source.id");
    }

    [Fact]
    public void Lint_accepts_good_profile()
    {
        Assert.DoesNotContain(new ProfileLinter().Lint(Sample()), f => f.Severity == LintSeverity.Error);
    }

    [Fact]
    public void Lint_rejects_invalid_name_characters()
    {
        var p = Sample();
        p.Metadata.Product = "7-Zip/Evil";
        Assert.Contains(new ProfileLinter().Lint(p), f => f.Path == "metadata.product");
    }

    [Fact]
    public void Package_naming_convention()
    {
        Assert.Equal("IgorPavlov_7-Zip_24.08_x64_MUI_PSADT_R1", PackageNaming.Build("Igor Pavlov", "7-Zip", "24.08", "x64", "MUI", 1));
    }
}
