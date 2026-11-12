namespace ModernWorkElevate.Core.Configuration;

using ModernWorkElevate.Core.Auth;

/// <summary>
/// Machine-wide settings an administrator sets through Group Policy or Intune
/// (ADMX: <c>packaging/policy/ModernWorkElevate.admx</c>). Read from
/// <c>HKLM\SOFTWARE\Policies\ModernWorkElevate</c> by the App project and applied to
/// <see cref="ModernWorkElevateOptions"/> after every configuration layer is bound, so a
/// per-user file can never override it.
/// </summary>
/// <remarks>
/// Device-code sign-in and a token cache on disk are not configurable at all —
/// both are permanently off (<see cref="ModernWorkElevateOptions.DeviceCodeAllowed"/>,
/// <see cref="ModernWorkElevateOptions.PersistentTokenCacheAllowed"/>). The policy only carries
/// the tenant list and its lock; any other value under the key is ignored.
/// </remarks>
public sealed record MachinePolicy
{
    /// <summary>Registry key below HKLM that holds the policy values.</summary>
    public const string RegistryKeyPath = @"SOFTWARE\Policies\ModernWorkElevate";

    /// <summary>Sub key whose values map tenant id (value name) to client id (value data).</summary>
    public const string TenantsSubKey = "Tenants";

    /// <summary>DWORD: 1 = only the tenants from the policy; the user cannot add or edit tenants.</summary>
    public const string LockTenantsValue = "LockTenants";

    /// <summary>The policy in effect when no policy key exists: the hardened defaults.</summary>
    public static MachinePolicy None { get; } = new();

    /// <summary>Tenants the organization pre-configures for every user of the machine.</summary>
    public IReadOnlyList<TenantAppRegistration> Tenants { get; init; } = [];

    /// <summary>When true, <see cref="Tenants"/> is the complete tenant list.</summary>
    public bool LockTenants { get; init; }

    /// <summary>
    /// Builds the policy from raw registry data. Never throws: a malformed entry is skipped
    /// and reported through <paramref name="skipped"/>, because a typo in a GPO must not keep
    /// the tray app from starting on every machine it reaches.
    /// </summary>
    /// <param name="flags">DWORD values of the policy key (value name → value).</param>
    /// <param name="tenants">
    /// Values of the <see cref="TenantsSubKey"/> sub key: tenant id → <c>ClientId</c>, optionally
    /// followed by <c>;Label</c> and <c>;Cloud</c> (e.g. <c>"…;Contoso;Global"</c>).
    /// </param>
    /// <param name="skipped">Tenant ids (value names) that could not be parsed.</param>
    /// <returns>The parsed policy.</returns>
    public static MachinePolicy Parse(
        IReadOnlyDictionary<string, object?> flags,
        IReadOnlyDictionary<string, string?> tenants,
        out IReadOnlyList<string> skipped)
    {
        ArgumentNullException.ThrowIfNull(flags);
        ArgumentNullException.ThrowIfNull(tenants);

        var parsed = new List<TenantAppRegistration>();
        var rejected = new List<string>();
        foreach (var (name, data) in tenants.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase))
        {
            var entry = ParseTenant(name, data);
            if (entry is null || parsed.Any(p => SameSlot(p, entry)))
            {
                rejected.Add(name);
                continue;
            }

            parsed.Add(entry);
        }

        skipped = rejected;
        return new MachinePolicy
        {
            Tenants = parsed,
            LockTenants = IsOn(flags, LockTenantsValue),
        };
    }

    /// <summary>True when both entries name the same (cloud, tenant) slot.</summary>
    internal static bool SameSlot(TenantAppRegistration a, TenantAppRegistration b)
        => Enum.TryParse<EntraCloud>(a.Cloud, ignoreCase: true, out var cloudA)
            && Guid.TryParse(a.TenantId, out var tenantA)
            && b.IsFor(cloudA, tenantA);

    private static TenantAppRegistration? ParseTenant(string name, string? data)
    {
        if (!Guid.TryParse(name, out var tenantId) || string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        var parts = data.Split(';', StringSplitOptions.TrimEntries);
        if (!Guid.TryParse(parts[0], out var clientId))
        {
            return null;
        }

        var label = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
        var cloud = EntraCloud.Global;
        if (parts.Length > 2 && parts[2].Length > 0 && !Enum.TryParse(parts[2], ignoreCase: true, out cloud))
        {
            return null;
        }

        return new TenantAppRegistration
        {
            TenantId = tenantId.ToString(),
            ClientId = clientId.ToString(),
            Cloud = cloud.ToString(),
            Label = label,
        };
    }

    private static bool IsOn(IReadOnlyDictionary<string, object?> flags, string name)
        => flags.TryGetValue(name, out var value) && value switch
        {
            int i => i != 0,
            long l => l != 0,
            string s => s.Trim() == "1" || (bool.TryParse(s, out var b) && b),
            _ => false,
        };
}
