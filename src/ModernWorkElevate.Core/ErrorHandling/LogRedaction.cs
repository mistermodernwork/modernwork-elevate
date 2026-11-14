namespace ModernWorkElevate.Core.ErrorHandling;

using System.Text.RegularExpressions;

/// <summary>
/// Masks user identifiers in free text before it reaches the log file. Graph, ARM and
/// Entra put the caller's UPN into their error messages — ARM's <c>AuthorizationFailed</c>
/// reads "The client 'alice@contoso.com' with object id …", AADSTS50020 names the user
/// account — and the log convention allows the object id only, never a UPN or mail.
/// Error codes, HTTP status, request ids, object ids and policy rule names stay
/// readable; only e-mail/UPN-shaped tokens are replaced.
/// </summary>
public static class LogRedaction
{
    /// <summary>What a masked UPN or mail address reads as in the log.</summary>
    public const string Placeholder = "[redacted]";

    // Local part: letters, digits and every character a UPN, mail address or guest UPN
    // may carry (alice_contoso.com#EXT#@fabrikam.onmicrosoft.com, john&jane@…) — except
    // quotes and backslashes, so "'alice@contoso.com'" keeps its quotes. The price: in
    // o'neill@contoso.com only "neill@contoso.com" is masked and "o'" stays, which does
    // not identify anyone. The domain needs at least one dot, which keeps
    // "@odata.type"-style tokens and a bare "@" untouched. NonBacktracking keeps the scan
    // linear in the message length; the timeout is the second line of defence.
    private static readonly Regex UpnPattern = new(
        @"[\p{L}\p{N}._%+#&!$*/=?^`{|}~-]+@[\p{L}\p{N}-]+(?:\.[\p{L}\p{N}-]+)+",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(250));

    /// <summary>
    /// Returns <paramref name="text"/> with every e-mail/UPN-shaped token replaced by
    /// <see cref="Placeholder"/>; <see langword="null"/> stays <see langword="null"/>.
    /// </summary>
    public static string? Redact(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('@', StringComparison.Ordinal))
        {
            return text;
        }

        try
        {
            return UpnPattern.Replace(text, Placeholder);
        }
        catch (RegexMatchTimeoutException)
        {
            // A message too odd to scan in time is dropped rather than logged unmasked.
            return Placeholder;
        }
    }

    /// <summary>
    /// The exception to hand to the logger: <paramref name="exception"/> itself when its
    /// text carries no UPN, otherwise a stand-in whose message and <see cref="Exception.ToString"/>
    /// (which is what the log sink writes, inner exceptions included) are masked. The
    /// stack trace is kept, so the log still shows where the failure came from.
    /// </summary>
    public static Exception ForLog(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var text = exception.ToString();
        var redacted = Redact(text)!;
        return string.Equals(text, redacted, StringComparison.Ordinal)
            ? exception
            : new RedactedException(exception, redacted);
    }

    /// <summary>
    /// Carries a masked copy of another exception into the log. It deliberately has no
    /// <see cref="Exception.InnerException"/>: a sink walking that chain would find the
    /// original, unmasked text again.
    /// </summary>
    private sealed class RedactedException : Exception
    {
        private readonly string _text;
        private readonly string? _stackTrace;

        public RedactedException(Exception original, string redactedText)
            : base(Redact(original.Message))
        {
            _text = redactedText;
            _stackTrace = original.StackTrace;
            HResult = original.HResult;
            Source = original.Source;
        }

        public override string? StackTrace => _stackTrace;

        public override string ToString() => _text;
    }
}
