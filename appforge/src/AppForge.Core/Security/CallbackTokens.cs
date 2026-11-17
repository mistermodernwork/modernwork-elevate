using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AppForge.Core.Security;

/// <summary>Inhalt eines Callback-Tokens (z. B. Freigabe-Button in Teams oder Logic-App-Rückruf).</summary>
public sealed record CallbackClaims(Guid RequestId, string Action, string? Audience, DateTimeOffset ExpiresAt, string Nonce);

/// <summary>
/// Kurzlebige, HMAC-SHA256-signierte Tokens für Rückrufe. Format: base64url(json).base64url(hmac).
/// Der Token berechtigt nur zu genau einer Aktion für genau eine Anfrage; die Identität des
/// Entscheiders kommt immer zusätzlich aus dem Kanal (Teams-Benutzer, Logic-App-Payload) und wird geprüft.
/// </summary>
public sealed class CallbackTokenService
{
    private readonly byte[] _key;
    private readonly TimeProvider _time;

    public CallbackTokenService(byte[] key, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 32)
        {
            throw new ArgumentException("Der Schlüssel muss mindestens 256 Bit lang sein.", nameof(key));
        }

        _key = key;
        _time = time ?? TimeProvider.System;
    }

    public string Create(Guid requestId, string action, TimeSpan lifetime, string? audience = null)
    {
        var claims = new CallbackClaims(requestId, action, audience, _time.GetUtcNow().Add(lifetime), Convert.ToHexString(RandomNumberGenerator.GetBytes(8)));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(claims));
        var signature = Base64Url(HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(payload)));
        return payload + "." + signature;
    }

    public bool TryValidate(string token, out CallbackClaims? claims, out string error)
    {
        claims = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(token) || token.Split('.') is not [var payload, var signature])
        {
            error = "Token-Format ungültig.";
            return false;
        }

        var expected = HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(payload));
        byte[] actual;
        try
        {
            actual = FromBase64Url(signature);
        }
        catch (FormatException)
        {
            error = "Signatur ungültig.";
            return false;
        }

        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            error = "Signatur ungültig.";
            return false;
        }

        try
        {
            claims = JsonSerializer.Deserialize<CallbackClaims>(FromBase64Url(payload));
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            error = "Inhalt ungültig.";
            return false;
        }

        if (claims is null || claims.ExpiresAt < _time.GetUtcNow())
        {
            error = "Token abgelaufen.";
            claims = null;
            return false;
        }

        return true;
    }

    /// <summary>HMAC-Signatur für ausgehende Webhooks (Header <c>X-AppForge-Signature: sha256=…</c>).</summary>
    public static string SignPayload(byte[] secret, string body) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    public static bool VerifyPayload(byte[] secret, string body, string header)
    {
        var expected = Encoding.ASCII.GetBytes(SignPayload(secret, body));
        var actual = Encoding.ASCII.GetBytes(header ?? string.Empty);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - (padded.Length % 4)) % 4);
        return Convert.FromBase64String(padded);
    }
}
