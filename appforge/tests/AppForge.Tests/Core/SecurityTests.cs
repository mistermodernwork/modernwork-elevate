using AppForge.Core.Audit;
using AppForge.Core.Domain;
using AppForge.Core.Processes;
using AppForge.Core.Security;
using AppForge.Tests.TestSupport;

namespace AppForge.Tests.Core;

public class AuditChainTests
{
    private static List<AuditEntryEntity> Chain(int n)
    {
        var list = new List<AuditEntryEntity>();
        string? prev = null;
        for (var i = 1; i <= n; i++)
        {
            var e = AuditChain.Seal(new AuditEntryEntity { Id = i, At = DateTimeOffset.UnixEpoch.AddMinutes(i), Actor = "a", Action = "x", SubjectType = "t", SubjectId = i.ToString(System.Globalization.CultureInfo.InvariantCulture), Data = "{}" }, prev);
            prev = e.Hash;
            list.Add(e);
        }

        return list;
    }

    [Fact]
    public void Intact_chain_verifies()
    {
        Assert.Null(AuditChain.Verify(Chain(5)));
    }

    [Fact]
    public void Tampered_data_is_detected()
    {
        var c = Chain(5);
        c[2].Data = "{\"evil\":true}";
        Assert.Equal(3, AuditChain.Verify(c));
    }

    [Fact]
    public void Deleted_entry_is_detected()
    {
        var c = Chain(5);
        c.RemoveAt(1);
        Assert.Equal(3, AuditChain.Verify(c));
    }
}

public class CallbackTokenTests
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void Valid_token_roundtrip()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var svc = new CallbackTokenService(Key, time);
        var id = Guid.NewGuid();
        var token = svc.Create(id, "approve", TimeSpan.FromHours(1), "teams");
        Assert.True(svc.TryValidate(token, out var claims, out _));
        Assert.Equal(id, claims!.RequestId);
        Assert.Equal("approve", claims.Action);
    }

    [Fact]
    public void Expired_token_is_rejected()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var svc = new CallbackTokenService(Key, time);
        var token = svc.Create(Guid.NewGuid(), "approve", TimeSpan.FromMinutes(5));
        time.Now = time.Now.AddMinutes(6);
        Assert.False(svc.TryValidate(token, out _, out var error));
        Assert.Contains("abgelaufen", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Tampered_token_is_rejected()
    {
        var svc = new CallbackTokenService(Key);
        var token = svc.Create(Guid.NewGuid(), "reject", TimeSpan.FromMinutes(5));
        var other = new CallbackTokenService(Enumerable.Repeat((byte)7, 32).ToArray());
        Assert.False(other.TryValidate(token, out _, out _));
        Assert.False(svc.TryValidate("abc", out _, out _));
        Assert.False(svc.TryValidate(token.Replace('.', 'x'), out _, out _));
        var parts = token.Split('.');
        Assert.False(svc.TryValidate(parts[0] + "x." + parts[1], out _, out _));
    }

    [Fact]
    public void Short_key_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new CallbackTokenService(new byte[16]));
    }

    [Fact]
    public void Webhook_payload_signature()
    {
        var sig = CallbackTokenService.SignPayload(Key, "{\"a\":1}");
        Assert.StartsWith("sha256=", sig, StringComparison.Ordinal);
        Assert.True(CallbackTokenService.VerifyPayload(Key, "{\"a\":1}", sig));
        Assert.False(CallbackTokenService.VerifyPayload(Key, "{\"a\":2}", sig));
    }

    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("with space", "\"with space\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData(@"C:\Path With\", "\"C:\\Path With\\\\\"")]
    [InlineData("", "\"\"")]
    public void Command_line_quoting_follows_windows_rules(string input, string expected)
    {
        Assert.Equal(expected, CommandLine.Quote(input));
    }
}
