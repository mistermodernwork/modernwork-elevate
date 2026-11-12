namespace ModernWorkElevate.Core.Configuration;

using System.Text.Json;
using System.Text.Json.Nodes;
using ModernWorkElevate.Core.Auth;

/// <summary>
/// Strongly-typed application configuration bound from the <c>ModernWorkElevate</c>
/// section of <c>appsettings.json</c> / <c>appsettings.local.json</c>.
/// </summary>
public sealed class ModernWorkElevateOptions
{
    /// <summary>Configuration section name this options object is bound from.</summary>
    public const string SectionName = "ModernWorkElevate";

    /// <summary>
    /// The page the shared-hosted admin-consent link returns to, published from <c>site/consent/</c>.
    /// Registered on the shared registration as its only Web redirect URI, so it never changes:
    /// released builds carry it.
    /// </summary>
    public const string SharedConsentPage = "https://login.microsoftonline.com/common/oauth2/nativeclient";

    /// <summary>
    /// Every App Registration the app may sign in with, each pinned to one tenant.
    /// A multi-tenant registration serving several tenants simply appears once per
    /// tenant with the same client id. This list is also the whitelist: a tenant
    /// without an entry cannot be enrolled. Never shipped in the default
    /// <c>appsettings.json</c>: <c>IConfiguration</c> overlays arrays index by index
    /// across layers, so a shipped placeholder entry would bleed into the user's first entry.
    /// </summary>
    /// <remarks>
    /// Pre-0.7.0 configurations carried one client id per cloud (<c>AppRegistrations:{Cloud}</c>,
    /// earlier a bare <c>ClientId</c>) plus an optional <c>AllowedTenants</c> whitelist.
    /// <see cref="LegacyRegistrationMigration"/> folds those into this list at startup.
    /// </remarks>
    public List<TenantAppRegistration> TenantAppRegistrations { get; set; } = [];

    /// <summary>Delegated Microsoft Graph scopes requested for PIM activation flows.</summary>
    public string[] Scopes { get; set; } = [];

    /// <summary>
    /// Client id of the multi-tenant App Registration a third party publishes, in canonical GUID
    /// form, or <c>null</c> when this build ships none — then the app offers no
    /// shared-hosted option at all. Global cloud only.
    /// </summary>
    /// <remarks>
    /// The private setter is the point: the configuration binder skips it, so neither
    /// <c>appsettings.local.json</c> can point "shared-hosted" — and the consent link the app
    /// hands to an admin — at another app. The only source is the shipped
    /// <c>appsettings.json</c>, read by <see cref="ReadShippedSharedHostedClientId"/> and
    /// applied through <see cref="UseSharedHostedClientId"/>.
    /// </remarks>
    public string? SharedHostedClientId { get; private set; }

    /// <summary>
    /// Whether the device-code flow may be offered and used: never in this app.
    /// Device code is phishable and cannot satisfy device-bound Conditional Access; sign-in
    /// goes through the Windows broker (WAM) only. A constant, so no configuration layer,
    /// policy or registry value can switch it on; <c>MsalAuthService</c> refuses the flow.
    /// </summary>
    public bool DeviceCodeAllowed => false;

    /// <summary>
    /// Whether MSAL may persist its token caches: never in this app. The broker
    /// (WAM) keeps the refresh tokens, and the app holds access tokens in memory only.
    /// <c>TokenCacheFactory</c> deletes cache files an earlier build left behind.
    /// </summary>
    public bool PersistentTokenCacheAllowed => false;

    /// <summary>
    /// True when a machine policy fixes the tenant list: the user can neither add, edit nor
    /// remove tenants, and per-user entries are ignored.
    /// </summary>
    public bool TenantsLockedByPolicy { get; private set; }

    /// <summary>
    /// Reads <c>ModernWorkElevate:SharedHostedClientId</c> from the shipped
    /// <c>appsettings.json</c> at <paramref name="appSettingsPath"/>, or <c>null</c> when the
    /// file or the key is missing or unreadable. Never throws: a broken value must not stop
    /// the app at launch, it only means no shared-hosted option.
    /// </summary>
    public static string? ReadShippedSharedHostedClientId(string appSettingsPath)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(appSettingsPath));
            return root?[SectionName]?["SharedHostedClientId"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sets <see cref="SharedHostedClientId"/>. Anything that is not a GUID counts as none:
    /// a shared-hosted entry would copy it into <c>ClientId</c>, and the validator rejects a
    /// non-GUID there at the next start.
    /// </summary>
    public void UseSharedHostedClientId(string? clientId)
        => SharedHostedClientId = Guid.TryParse(clientId, out var parsed) ? parsed.ToString() : null;

    /// <summary>
    /// Applies the machine-wide policy on top of the bound configuration. Runs as a
    /// <c>PostConfigure</c> step, after every JSON layer and before validation.
    /// </summary>
    /// <remarks>
    /// Policy tenants are merged here rather than added as a configuration layer:
    /// <c>IConfiguration</c> overlays arrays index by index, so a policy layer would blend
    /// field by field into the user's entries. A policy entry replaces a per-user entry for
    /// the same (cloud, tenant); with <see cref="MachinePolicy.LockTenants"/> the policy list
    /// is the whole list.
    /// </remarks>
    public void ApplyMachinePolicy(MachinePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        TenantsLockedByPolicy = policy.LockTenants;

        var policyEntries = policy.Tenants.Select(Copy).ToList();
        if (policy.LockTenants)
        {
            TenantAppRegistrations = policyEntries;
            return;
        }

        TenantAppRegistrations.RemoveAll(existing => policyEntries.Any(p => MachinePolicy.SameSlot(p, existing)));
        TenantAppRegistrations.AddRange(policyEntries);

        static TenantAppRegistration Copy(TenantAppRegistration source) => new()
        {
            TenantId = source.TenantId,
            ClientId = source.ClientId,
            Cloud = source.Cloud,
            Label = source.Label,
        };
    }

    /// <summary>
    /// Client id to authenticate with for <paramref name="tenantId"/> in <paramref name="cloud"/>,
    /// or <c>null</c> when no registration is pinned to that tenant. This is the whole
    /// login → registration mapping: an enrollment is identified by (oid, tenantId, cloud),
    /// so its registration is derived from configuration on every call and nothing about
    /// it is persisted per account.
    /// </summary>
    public string? ClientIdFor(EntraCloud cloud, string? tenantId)
    {
        if (!Guid.TryParse(tenantId, out var tenant))
        {
            return null;
        }

        return TenantAppRegistrations.FirstOrDefault(r => r.IsFor(cloud, tenant))?.ClientId.Trim();
    }

    /// <summary>
    /// Clouds with at least one registration, in <see cref="EntraCloud"/> declaration
    /// order. Drives the first-run configuration gate and the network diagnostics.
    /// </summary>
    public IReadOnlyList<EntraCloud> ConfiguredClouds()
        => [.. Enum.GetValues<EntraCloud>().Where(c => TenantAppRegistrations.Any(r => r.IsFor(c)))];

    /// <summary>
    /// Whose registration <paramref name="clientId"/> in <paramref name="cloud"/> is. The one
    /// rule behind the hosting chip, the device-code gate, the error wording and the consent
    /// link: shared-hosted only in the Global cloud and only for the shipped shared client id.
    /// </summary>
    public AppRegistrationHosting HostingOf(EntraCloud cloud, string? clientId)
        => cloud == EntraCloud.Global
            && Guid.TryParse(SharedHostedClientId, out var shared)
            && Guid.TryParse(clientId, out var parsed)
            && parsed == shared
                ? AppRegistrationHosting.SharedHosted
                : AppRegistrationHosting.SelfHosted;

    /// <summary>
    /// Whose registration the tenant <paramref name="tenantId"/> in <paramref name="cloud"/>
    /// signs in with, as configured at startup. A tenant without an entry counts as self-hosted.
    /// </summary>
    public AppRegistrationHosting HostingFor(EntraCloud cloud, string? tenantId)
        => HostingOf(cloud, ClientIdFor(cloud, tenantId));

    /// <summary>
    /// The scopes the interactive sign-in asks for. Shared-hosted asks for Microsoft Graph's
    /// <c>.default</c>: on a tenant's first consent Entra then lists every permission the
    /// registration declares, Azure Service Management included, so one admin consent in the
    /// sign-in window covers Azure as well (field test 2026-10-20). The token stays Graph-only.
    /// Self-hosted keeps the explicit list: a customer's registration may declare more than the
    /// app needs, and <c>.default</c> would consent to it and put all of it into every token.
    /// </summary>
    /// <remarks>
    /// Only a first consent prompts. A tenant that has already granted Graph sees no prompt, so
    /// its Azure permission still comes from the consent link.
    /// </remarks>
    public string[] EnrollmentScopes(EntraCloud cloud, string? clientId)
        => HostingOf(cloud, clientId) == AppRegistrationHosting.SharedHosted
            ? ["https://graph.microsoft.com/.default"]
            : Scopes;

    /// <summary>
    /// The client id a new or edited entry gets when the user chose <paramref name="hosting"/>
    /// in <paramref name="cloud"/> and typed <paramref name="typedClientId"/>, or <c>null</c>
    /// when that combination is not valid. Shared-hosted ignores the typed value and needs the
    /// Global cloud and a shipped shared client id; self-hosted needs a GUID, trimmed as typed.
    /// One rule for the add form and the gear editor, so neither can save an entry the next
    /// start rejects.
    /// </summary>
    public string? ResolveClientId(AppRegistrationHosting hosting, EntraCloud cloud, string? typedClientId)
        => hosting == AppRegistrationHosting.SharedHosted
            ? cloud == EntraCloud.Global ? SharedHostedClientId : null
            : Guid.TryParse(typedClientId, out _) ? typedClientId!.Trim() : null;

    /// <summary>
    /// The admin-consent link for the shared-hosted registration in <paramref name="tenantId"/>,
    /// or <c>null</c> outside the Global cloud, for a tenant id that is not a GUID, or when this
    /// build ships no shared client id. Same parameters as the link in
    /// <c>docs/app-registration-setup.md</c>. The redirect URI is the shared registration's consent page
    /// (<see cref="SharedConsentPage"/>), which tells the admin whether the consent went through;
    /// the broker URI left the browser loading forever (field test 2026-10-17).
    /// </summary>
    public string? SharedAdminConsentUrl(EntraCloud cloud, string? tenantId)
    {
        if (cloud != EntraCloud.Global || SharedHostedClientId is not { } clientId || !Guid.TryParse(tenantId, out var tenant))
        {
            return null;
        }

        var redirectUri = Uri.EscapeDataString(SharedConsentPage);
        return $"{EntraCloudInfo.AuthorityBaseUrl(cloud)}/{tenant}/adminconsent?client_id={clientId}&redirect_uri={redirectUri}";
    }
}
