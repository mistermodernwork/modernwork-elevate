using AppForge.Core.Abstractions;
using AppForge.Infrastructure.Audit;
using AppForge.Infrastructure.Data;
using AppForge.Infrastructure.Queue;
using AppForge.Infrastructure.Secrets;
using AppForge.Infrastructure.Storage;
using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AppForge.Infrastructure;

/// <summary>
/// Registriert alle Infrastruktur-Dienste passend zum Betriebsmodus (Plan 3.2a/3.2b).
/// Jede Abhängigkeit ist einzeln umschaltbar; <c>Hosting:Mode</c> setzt nur die Standardwerte.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddAppForgeInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var hosting = configuration.GetSection(HostingOptions.Section).Get<HostingOptions>() ?? new HostingOptions();
        services.AddSingleton(hosting);
        services.TryAddSingleton(TimeProvider.System);
        var cloud = hosting.Mode != HostingMode.OnPrem;
        var dataDir = Path.GetFullPath(hosting.DataDirectory);

        services.TryAddSingleton<TokenCredential>(_ => new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = configuration["Azure:ManagedIdentityClientId"],
        }));

        // Datenbank
        var provider = configuration["Database:Provider"] ?? (cloud ? "SqlServer" : "Sqlite");
        var connection = configuration.GetConnectionString("AppForge") ?? $"Data Source={Path.Combine(dataDir, "appforge.db")}";
        if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(dataDir);
        }

        services.AddDbContextFactory<AppForgeDbContext>(o =>
        {
            if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                o.UseSqlServer(connection, sql => sql.EnableRetryOnFailure());
            }
            else
            {
                o.UseSqlite(connection);
            }
        });
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppForgeDbContext>>().CreateDbContext());

        // Artefakte
        var artifacts = configuration["Artifacts:Provider"] ?? (cloud ? "Blob" : "FileSystem");
        if (artifacts.Equals("Blob", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IArtifactStore>(sp =>
            {
                var uri = configuration["Artifacts:ContainerUri"] ?? throw new InvalidOperationException("Artifacts:ContainerUri fehlt.");
                return new BlobArtifactStore(new BlobContainerClient(new Uri(uri), sp.GetRequiredService<TokenCredential>()));
            });
        }
        else
        {
            services.AddSingleton<IArtifactStore>(_ => new FileSystemArtifactStore(configuration["Artifacts:Path"] ?? Path.Combine(dataDir, "artifacts")));
        }

        // Warteschlange
        var queue = configuration["Queue:Provider"] ?? (cloud ? "ServiceBus" : "Database");
        if (queue.Equals("ServiceBus", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton(sp => new ServiceBusClient(
                configuration["Queue:Namespace"] ?? throw new InvalidOperationException("Queue:Namespace fehlt (z. B. contoso.servicebus.windows.net)."),
                sp.GetRequiredService<TokenCredential>()));
            services.AddSingleton<IJobQueue>(sp => new ServiceBusJobQueue(
                sp.GetRequiredService<IDbContextFactory<AppForgeDbContext>>(),
                sp.GetRequiredService<ServiceBusClient>(),
                configuration["Queue:Name"] ?? "appforge-jobs",
                sp.GetRequiredService<TimeProvider>()));
        }
        else
        {
            services.AddSingleton<IJobQueue, DbJobQueue>();
        }

        // Secrets
        var secrets = configuration["Secrets:Provider"] ?? (cloud ? "KeyVault" : "DataProtection");
        if (secrets.Equals("KeyVault", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<ISecretStore>(sp => new KeyVaultSecretStore(new SecretClient(
                new Uri(configuration["Secrets:VaultUri"] ?? throw new InvalidOperationException("Secrets:VaultUri fehlt.")),
                sp.GetRequiredService<TokenCredential>())));
        }
        else
        {
            var keyDir = Path.Combine(dataDir, "keys");
            var dp = services.AddDataProtection().SetApplicationName("ModernWork AppForge").PersistKeysToFileSystem(new DirectoryInfo(keyDir));
            if (OperatingSystem.IsWindows())
            {
                dp.ProtectKeysWithDpapi(protectToLocalMachine: true);
            }

            services.AddSingleton<ISecretStore>(sp => new DataProtectionSecretStore(sp.GetRequiredService<IDataProtectionProvider>(), Path.Combine(dataDir, "secrets")));
        }

        services.AddSingleton<DbAuditLog>();
        services.AddSingleton<IAuditLog>(sp => sp.GetRequiredService<DbAuditLog>());
        return services;
    }

    /// <summary>Legt das Schema an (v1: EnsureCreated; Migrationen siehe README „Offen“).</summary>
    public static async Task EnsureDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<AppForgeDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);
    }
}
