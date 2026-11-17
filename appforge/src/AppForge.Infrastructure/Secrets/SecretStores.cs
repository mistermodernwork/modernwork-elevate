using System.Text.Json;
using AppForge.Core.Abstractions;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.DataProtection;

namespace AppForge.Infrastructure.Secrets;

/// <summary>
/// Lokaler Secret-Speicher (On-Prem): Werte werden mit ASP.NET Core Data Protection verschlüsselt
/// (unter Windows zusätzlich DPAPI-Machine-Scope für den Schlüsselring, siehe Registrierung).
/// </summary>
public sealed class DataProtectionSecretStore : ISecretStore
{
    private readonly IDataProtector _protector;
    private readonly string _file;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DataProtectionSecretStore(IDataProtectionProvider provider, string directory)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector("AppForge.Secrets.v1");
        Directory.CreateDirectory(directory);
        _file = Path.Combine(directory, "secrets.json");
    }

    public async Task<string?> GetAsync(string name, CancellationToken ct = default)
    {
        var all = await LoadAsync(ct).ConfigureAwait(false);
        return all.TryGetValue(name, out var cipher) ? _protector.Unprotect(cipher) : null;
    }

    public async Task SetAsync(string name, string value, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var all = await LoadAsync(ct).ConfigureAwait(false);
            all[name] = _protector.Protect(value);
            await File.WriteAllTextAsync(_file, JsonSerializer.Serialize(all), ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var all = await LoadAsync(ct).ConfigureAwait(false);
            if (all.Remove(name))
            {
                await File.WriteAllTextAsync(_file, JsonSerializer.Serialize(all), ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken ct) =>
        File.Exists(_file)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(_file, ct).ConfigureAwait(false)) ?? []
            : [];
}

/// <summary>Azure Key Vault (RBAC-Modus, Managed Identity).</summary>
public sealed class KeyVaultSecretStore(SecretClient client) : ISecretStore
{
    public async Task<string?> GetAsync(string name, CancellationToken ct = default)
    {
        try
        {
            return (await client.GetSecretAsync(Normalize(name), cancellationToken: ct).ConfigureAwait(false)).Value.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public Task SetAsync(string name, string value, CancellationToken ct = default) =>
        client.SetSecretAsync(Normalize(name), value, ct);

    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        try
        {
            await client.StartDeleteSecretAsync(Normalize(name), ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // schon weg
        }
    }

    /// <summary>Key Vault erlaubt nur [0-9a-zA-Z-].</summary>
    internal static string Normalize(string name) =>
        new(name.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());
}
