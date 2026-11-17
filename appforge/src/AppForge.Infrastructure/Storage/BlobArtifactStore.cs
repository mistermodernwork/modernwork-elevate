using System.Runtime.CompilerServices;
using AppForge.Core.Abstractions;
using AppForge.Core.Security;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace AppForge.Infrastructure.Storage;

/// <summary>
/// Artefaktspeicher in Azure Blob Storage. Der Container ist per Bicep immutable (zeitbasierte
/// Aufbewahrung) und versioniert; zusätzlich verhindert <c>If-None-Match: *</c> jedes Überschreiben.
/// </summary>
public sealed class BlobArtifactStore(BlobContainerClient container) : IArtifactStore
{
    public async Task<StoredArtifact> PutAsync(string key, Stream content, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var temp = Path.GetTempFileName();
        try
        {
            await using (var fs = File.Create(temp))
            {
                await content.CopyToAsync(fs, ct).ConfigureAwait(false);
            }

            var sha = await Hashing.Sha256FileAsync(temp, ct).ConfigureAwait(false);
            var blob = container.GetBlobClient(key);
            await using var read = File.OpenRead(temp);
            try
            {
                await blob.UploadAsync(read, new BlobUploadOptions
                {
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                    Metadata = new Dictionary<string, string> { ["sha256"] = sha },
                }, ct).ConfigureAwait(false);
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                throw new InvalidOperationException($"Artefakt '{key}' existiert bereits (Speicher ist unveränderlich).", ex);
            }

            return new StoredArtifact(key, sha, new FileInfo(temp).Length);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) =>
        await container.GetBlobClient(key).OpenReadAsync(cancellationToken: ct).ConfigureAwait(false);

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        (await container.GetBlobClient(key).ExistsAsync(ct).ConfigureAwait(false)).Value;

    public async IAsyncEnumerable<string> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var item in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, ct).ConfigureAwait(false))
        {
            yield return item.Name;
        }
    }
}
