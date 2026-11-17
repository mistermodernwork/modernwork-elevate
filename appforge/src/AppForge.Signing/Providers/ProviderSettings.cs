using System.Text.Json;
using AppForge.Core.Domain;

namespace AppForge.Signing.Providers;

/// <summary>Liest die Provider-Einstellungen (SettingsJson) eines Zertifikats.</summary>
public sealed class ProviderSettings
{
    private readonly Dictionary<string, string> _values;

    private ProviderSettings(Dictionary<string, string> values) => _values = values;

    public static ProviderSettings From(SigningCertificateEntity certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(certificate.SettingsJson))
        {
            using var doc = JsonDocument.Parse(certificate.SettingsJson);
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                values[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText();
            }
        }

        return new ProviderSettings(values);
    }

    public string? Get(string key) => _values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    public string Require(string key) => Get(key) ?? throw new InvalidOperationException($"Einstellung '{key}' fehlt am Zertifikat.");
}
