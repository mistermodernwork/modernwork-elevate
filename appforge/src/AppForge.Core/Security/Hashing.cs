using System.Security.Cryptography;

namespace AppForge.Core.Security;

public static class Hashing
{
    public static async Task<string> Sha256Async(Stream stream, CancellationToken ct = default)
    {
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    public static async Task<string> Sha256FileAsync(string path, CancellationToken ct = default)
    {
        await using var fs = File.OpenRead(path);
        return await Sha256Async(fs, ct).ConfigureAwait(false);
    }

    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
}
