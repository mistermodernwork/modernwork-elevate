namespace ModernWorkElevate.Tests.ErrorHandling;

using ModernWorkElevate.Core.Arm;
using ModernWorkElevate.Core.ErrorHandling;

public sealed class LogRedactionTests
{
    [Fact]
    public void Redact_UpnInQuotes_IsMaskedAndTheRestKept()
    {
        var text = "The client 'alice@contoso.com' with object id '0f5c2d1e-1111-2222-3333-444455556666' does not have authorization.";

        var redacted = LogRedaction.Redact(text);

        Assert.Equal(
            "The client '[redacted]' with object id '0f5c2d1e-1111-2222-3333-444455556666' does not have authorization.",
            redacted);
    }

    [Theory]
    [InlineData("Contact alice.smith@contoso.co.uk for access.", "Contact [redacted] for access.")]
    [InlineData("User alice@contoso.com.", "User [redacted].")]
    [InlineData("AADSTS50020: User account 'alice@contoso.com' from identity provider 'live.com' does not exist.", "AADSTS50020: User account '[redacted]' from identity provider 'live.com' does not exist.")]
    [InlineData("Guest alice_contoso.com#EXT#@fabrikam.onmicrosoft.com is blocked.", "Guest [redacted] is blocked.")]
    [InlineData("a@b.com and b+tag@c.org", "[redacted] and [redacted]")]
    [InlineData("Mailbox john&jane@contoso.com is shared.", "Mailbox [redacted] is shared.")]
    [InlineData("Mailbox it!ops$*/=?^`{|}~@contoso.com is shared.", "Mailbox [redacted] is shared.")]
    public void Redact_MailAndUpnShapes_AreMasked(string text, string expected)
        => Assert.Equal(expected, LogRedaction.Redact(text));

    [Theory]
    [InlineData("The following policy rules failed: [\"MfaRule\"]")]
    [InlineData("Object id 0f5c2d1e-1111-2222-3333-444455556666, request-id 9a8b7c6d-0000-1111-2222-333344445555.")]
    [InlineData("The query pattern is not supported; see @odata.type for details.")]
    [InlineData("Scope '/subscriptions/sub-1/resourceGroups/rg-1' is invalid.")]
    [InlineData("claims=%7B%22access_token%22%3A%7B%22acrs%22%7D%7D")]
    [InlineData("")]
    public void Redact_TextWithoutAUpn_IsUnchanged(string text)
        => Assert.Equal(text, LogRedaction.Redact(text));

    [Fact]
    public void Redact_ApostropheInTheLocalPart_LeavesOnlyTheFragmentBeforeIt()
    {
        // Accepted trade-off: the apostrophe is excluded so quoted UPNs keep their quotes;
        // "o'" alone does not identify anyone.
        Assert.Equal("User o'[redacted] failed.", LogRedaction.Redact("User o'neill@contoso.com failed."));
    }

    [Fact]
    public void Redact_LongTextWithoutAMatch_ReturnsQuickly()
    {
        var text = new string('a', 100_000) + "@" + new string('b', 100_000);

        Assert.Equal(text, LogRedaction.Redact(text));
    }

    [Fact]
    public void Redact_Null_StaysNull()
        => Assert.Null(LogRedaction.Redact(null));

    [Fact]
    public void ForLog_ExceptionWithoutAUpn_IsReturnedAsIs()
    {
        var error = new ArmRequestException(503, "ServiceUnavailable", "Try again later.");

        Assert.Same(error, LogRedaction.ForLog(error));
    }

    [Fact]
    public void ForLog_ExceptionWithAUpn_IsMaskedButKeepsCodeTypeAndStackTrace()
    {
        ArmRequestException thrown;
        try
        {
            throw new ArmRequestException(403, "AuthorizationFailed", "The client 'alice@contoso.com' with object id 'oid-1' does not have authorization.");
        }
        catch (ArmRequestException ex)
        {
            thrown = ex;
        }

        var forLog = LogRedaction.ForLog(thrown);

        Assert.NotSame(thrown, forLog);
        Assert.DoesNotContain("@contoso.com", forLog.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("@contoso.com", forLog.Message, StringComparison.Ordinal);
        Assert.Contains("AuthorizationFailed", forLog.ToString(), StringComparison.Ordinal);
        Assert.Contains(nameof(ArmRequestException), forLog.ToString(), StringComparison.Ordinal);
        Assert.Contains("oid-1", forLog.ToString(), StringComparison.Ordinal);
        Assert.Equal(thrown.StackTrace, forLog.StackTrace);
        Assert.Null(forLog.InnerException);
    }

    [Fact]
    public void ForLog_UpnInAnInnerException_IsMasked()
    {
        var inner = new ArmRequestException(403, "AuthorizationFailed", "The client 'alice@contoso.com' is not authorized.");
        var outer = new InvalidOperationException("Fetch failed.", inner);

        var forLog = LogRedaction.ForLog(outer);

        Assert.DoesNotContain("@contoso.com", forLog.ToString(), StringComparison.Ordinal);
        Assert.Contains("Fetch failed.", forLog.ToString(), StringComparison.Ordinal);
    }
}
