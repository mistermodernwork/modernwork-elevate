using System.Runtime.CompilerServices;
using AppForge.Core.Abstractions;
using AppForge.Core.Security;

namespace AppForge.Infrastructure.Storage;

/// <summary>
/// Artefaktspeicher auf Dateisystem/SMB-Share (On-Prem). Schreibt atomar über eine temporäre Datei,
/// überschreibt nie und setzt fertige Dateien schreibgeschützt.
/// </summary>
public sealed class FileSystemArtifactStore : IArtifactStore
{
    private readonly string _root;

    public FileSystemArtifactStore(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task<StoredArtifact> PutAsync(string key, Stream content, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = Resolve(key);
        if (File.Exists(path))
        {
            throw new InvalidOperationException($"Artefakt '{key}' existiert bereits (Speicher ist unveränderlich).");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(fs, ct).ConfigureAwait(false);
            }

            var sha = await Hashing.Sha256FileAsync(temp, ct).ConfigureAwait(false);
            File.Move(temp, path, overwrite: false);
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            return new StoredArtifact(key, sha, new FileInfo(path).Length);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) =>
        Task.FromResult<Stream>(new FileStream(Resolve(key), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(File.Exists(Resolve(key)));

    public async IAsyncEnumerable<string> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var dir = Resolve(prefix.TrimEnd('/') is { Length: > 0 } p ? p : ".");
        if (!Directory.Exists(dir))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            if (!file.EndsWith(".tmp", StringComparison.Ordinal))
            {
                yield return Path.GetRelativePath(_root, file).Replace('\\', '/');
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    internal string Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(key) || key.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException($"Ungültiger Artefakt-Schlüssel '{key}'.", nameof(key));
        }

        var full = Path.GetFullPath(Path.Combine(_root, key.Replace('\\', '/')));
        if (!full.StartsWith(_root, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Ungültiger Artefakt-Schlüssel '{key}'.", nameof(key));
        }

        return full;
    }
}
