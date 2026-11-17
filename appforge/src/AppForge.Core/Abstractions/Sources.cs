using AppForge.Core.Domain;
using AppForge.Core.Profiles;

namespace AppForge.Core.Abstractions;

/// <summary>Eine verfügbare Version laut Quelle.</summary>
public sealed record SourceVersion
{
    public required string Version { get; init; }

    public required string DownloadUrl { get; init; }

    public string? Sha256 { get; init; }

    public string Architecture { get; init; } = "x64";

    public InstallerType InstallerType { get; init; } = InstallerType.Unknown;

    public string? SilentArguments { get; init; }

    public string? ProductCode { get; init; }

    public string? Publisher { get; init; }

    public string? PackageName { get; init; }

    public string? Description { get; init; }

    public string? Homepage { get; init; }

    public string? License { get; init; }

    public string? Locale { get; init; }

    public string? FileName { get; init; }
}

public interface ISourceProvider
{
    SourceProviderType Type { get; }

    Task<IReadOnlyList<SourceVersion>> GetVersionsAsync(SourceSettings source, CancellationToken ct = default);
}

public interface ISourceProviderFactory
{
    ISourceProvider Get(SourceProviderType type);
}
