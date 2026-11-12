namespace ModernWorkElevate.AppAvalonia.Services;

using System.Collections.Generic;
using Microsoft.Win32;
using ModernWorkElevate.Core.Configuration;

/// <summary>
/// Reads the machine-wide policy from <c>HKLM\SOFTWARE\Policies\ModernWorkElevate</c>
/// (set by Group Policy via the shipped ADMX, by Intune ADMX ingestion or a
/// Settings Catalog custom setting). Only HKLM counts: the policy key is writable by
/// administrators only, so a standard user cannot weaken it. Parsing lives in Core
/// (<see cref="MachinePolicy.Parse"/>), where it is unit tested.
/// </summary>
internal static class MachinePolicyReader
{
    /// <summary>
    /// Returns the policy, or <see cref="MachinePolicy.None"/> (the hardened defaults) when
    /// no policy key exists or it cannot be read. Never throws.
    /// </summary>
    /// <param name="skipped">Tenant value names that could not be parsed.</param>
    public static MachinePolicy Read(out IReadOnlyList<string> skipped)
    {
        skipped = [];
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(MachinePolicy.RegistryKeyPath);
            if (key is null)
            {
                return MachinePolicy.None;
            }

            var flags = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in key.GetValueNames())
            {
                flags[name] = key.GetValue(name);
            }

            var tenants = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            using (var tenantKey = key.OpenSubKey(MachinePolicy.TenantsSubKey))
            {
                if (tenantKey is not null)
                {
                    foreach (var name in tenantKey.GetValueNames())
                    {
                        tenants[name] = tenantKey.GetValue(name) as string;
                    }
                }
            }

            return MachinePolicy.Parse(flags, tenants, out skipped);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return MachinePolicy.None;
        }
    }
}
