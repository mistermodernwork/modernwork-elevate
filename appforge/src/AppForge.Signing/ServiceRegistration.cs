using AppForge.Core.Abstractions;
using AppForge.Core.Processes;
using AppForge.Signing.Checks;
using AppForge.Signing.Providers;
using AppForge.Signing.Verification;
using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AppForge.Signing;

public static class ServiceRegistration
{
    public static IServiceCollection AddAppForgeSigning(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var options = configuration.GetSection(SigningOptions.Section).Get<SigningOptions>() ?? new SigningOptions();
        services.AddSingleton(options);
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<ISigningProvider, CertStoreSigningProvider>();
        services.AddSingleton<ISigningProvider, KeyVaultSigningProvider>();
        services.AddSingleton<ISigningProvider, ArtifactSigningProvider>();
        services.AddSingleton<ISigningProvider, PfxSigningProvider>();
        services.AddSingleton<ISigningProvider, ExternalCaSigningProvider>();
        services.AddSingleton<IExternalCaPlugin, JsignExternalCaPlugin>();
        services.AddSingleton<SigningProviderFactory>();
        services.AddSingleton<KeyVaultCsrService>();
        services.TryAddSingleton<ISignatureVerifier>(_ => new AuthenticodeInspector());

        var scanner = configuration["Malware:Provider"] ?? "None";
        switch (scanner.ToUpperInvariant())
        {
            case "DEFENDERFORSTORAGE":
                services.TryAddSingleton<IMalwareScanner>(sp => new DefenderForStorageScanner(new BlobContainerClient(
                    new Uri(configuration["Artifacts:ContainerUri"] ?? throw new InvalidOperationException("Artifacts:ContainerUri fehlt.")),
                    sp.GetRequiredService<TokenCredential>())));
                break;
            case "DEFENDERCLI":
                services.TryAddSingleton<IMalwareScanner>(sp => new DefenderCliScanner(sp.GetRequiredService<IProcessRunner>(), configuration["Malware:MpCmdRunPath"]));
                break;
            default:
                services.TryAddSingleton<IMalwareScanner, DevelopmentNoScanScanner>();
                break;
        }

        if (configuration.GetValue("Signing:UsePsScriptAnalyzer", true))
        {
            services.TryAddSingleton<IScriptAnalyzer>(sp => new PsScriptAnalyzer(sp.GetRequiredService<IProcessRunner>(), options, configuration["Signing:AnalyzerSettings"]));
        }

        services.AddSingleton(sp => new SigningPipeline(
            options,
            sp.GetRequiredService<SigningProviderFactory>(),
            sp.GetRequiredService<ISignatureVerifier>(),
            sp.GetRequiredService<IMalwareScanner>(),
            sp.GetService<IScriptAnalyzer>()));
        return services;
    }
}
