using AppForge.Signing.Engines;

namespace AppForge.Tests.Signing;

public class SigningCommandTests
{
    [Fact]
    public void Jsign_trusted_signing_uses_env_for_token_and_rfc3161()
    {
        var spec = JsignCommand.Build("java", "/opt/jsign/jsign.jar", new JsignCall
        {
            StoreType = "TRUSTEDSIGNING",
            Keystore = "weu.codesigning.azure.net",
            Alias = "contoso/private-trust",
            StorePassword = "secret-token",
            TimestampUrl = "http://timestamp.acs.microsoft.com",
            Files = ["/work/a.ps1", "/work/b.dll"],
        });

        Assert.Equal("java", spec.FileName);
        Assert.Equal(["-jar", "/opt/jsign/jsign.jar", "--storetype", "TRUSTEDSIGNING", "--keystore", "weu.codesigning.azure.net"], spec.Arguments.Take(6));
        Assert.DoesNotContain("secret-token", spec.Arguments);
        Assert.Contains("env:" + JsignCommand.StorePassVariable, spec.Arguments);
        Assert.Equal("secret-token", spec.Environment![JsignCommand.StorePassVariable]);
        Assert.Contains("RFC3161", spec.Arguments);
        Assert.Contains("SHA-256", spec.Arguments);
        Assert.Equal(["/work/a.ps1", "/work/b.dll"], spec.Arguments.TakeLast(2));
        Assert.DoesNotContain("secret-token", spec.ToDisplayString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Jsign_native_launcher_is_called_directly()
    {
        var spec = JsignCommand.Build("java", "/usr/bin/jsign", new JsignCall { StoreType = "PKCS12", Files = ["x.exe"] });
        Assert.Equal("/usr/bin/jsign", spec.FileName);
        Assert.Equal("--storetype", spec.Arguments[0]);
    }

    [Fact]
    public void Jsign_requires_files()
    {
        Assert.Throws<ArgumentException>(() => JsignCommand.Build("java", "j.jar", new JsignCall { StoreType = "PKCS12", Files = [] }));
    }

    [Fact]
    public void Signtool_cert_store_matches_elevate_build_script()
    {
        var spec = SignToolCommand.BuildSign(@"C:\sdk\signtool.exe", new SignToolCall
        {
            Thumbprint = "ABCDEF",
            TimestampUrl = "http://timestamp.digicert.com",
            Description = "Contoso App",
            Files = [@"C:\w\a.ps1"],
        });
        Assert.Equal(["sign", "/fd", "SHA256", "/sha1", "ABCDEF", "/sm", "/tr", "http://timestamp.digicert.com", "/td", "SHA256", "/d", "Contoso App", @"C:\w\a.ps1"], spec.Arguments);
    }

    [Fact]
    public void Signtool_pfx_password_is_masked()
    {
        var spec = SignToolCommand.BuildSign("signtool", new SignToolCall { PfxFile = "t.pfx", PfxPassword = "geheim123", Files = ["a.exe"] });
        Assert.Contains("geheim123", spec.Arguments);
        Assert.DoesNotContain("geheim123", spec.ToDisplayString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Signtool_artifact_signing_uses_dlib()
    {
        var spec = SignToolCommand.BuildSign("signtool", new SignToolCall { DlibPath = "dlib.dll", DlibMetadataFile = "m.json", Files = ["a.exe"] });
        Assert.Equal(["sign", "/fd", "SHA256", "/dlib", "dlib.dll", "/dmdf", "m.json", "a.exe"], spec.Arguments);
    }

    [Fact]
    public void Signtool_without_identity_throws()
    {
        Assert.Throws<ArgumentException>(() => SignToolCommand.BuildSign("signtool", new SignToolCall { Files = ["a.exe"] }));
    }

    [Fact]
    public void PowerShell_fallback_quotes_paths()
    {
        var spec = PowerShellSignCommand.Build("pwsh", "ABC", null, ["C:\\it's\\a.ps1"]);
        var command = spec.Arguments[^1];
        Assert.Contains("'C:\\it''s\\a.ps1'", command, StringComparison.Ordinal);
        Assert.Contains("Set-AuthenticodeSignature", command, StringComparison.Ordinal);
    }
}
