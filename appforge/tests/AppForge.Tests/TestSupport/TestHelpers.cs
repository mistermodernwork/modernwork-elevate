using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AppForge.Core.Processes;
using AppForge.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AppForge.Tests.TestSupport;

public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "appforge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            {
                System.IO.File.SetAttributes(f, FileAttributes.Normal);
            }

            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Zeichnet Prozessaufrufe auf und liefert vorgegebene Ergebnisse.</summary>
public sealed class RecordingProcessRunner : IProcessRunner
{
    public List<ProcessSpec> Calls { get; } = [];

    public Func<ProcessSpec, ProcessResult> Handler { get; set; } = _ => new ProcessResult(0, string.Empty, string.Empty, false);

    public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
    {
        Calls.Add(spec);
        return Task.FromResult(Handler(spec));
    }
}

/// <summary>SQLite-Datenbank in einer Temp-Datei (echtes SQL, keine In-Memory-Attrappe).</summary>
public sealed class SqliteDb : IDisposable
{
    private readonly TempDir _dir = new();

    public SqliteDb()
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = _dir.File("test.db"), Pooling = false }.ToString();
        Options = new DbContextOptionsBuilder<AppForgeDbContext>().UseSqlite(cs).Options;
        using var db = new AppForgeDbContext(Options);
        db.Database.EnsureCreated();
        Factory = new DbFactory(Options);
    }

    public DbContextOptions<AppForgeDbContext> Options { get; }

    public IDbContextFactory<AppForgeDbContext> Factory { get; }

    public AppForgeDbContext Create() => new(Options);

    public void Dispose() => _dir.Dispose();

    private sealed class DbFactory(DbContextOptions<AppForgeDbContext> options) : IDbContextFactory<AppForgeDbContext>
    {
        public AppForgeDbContext CreateDbContext() => new(options);
    }
}

public static class TestCertificates
{
    /// <summary>Selbstsigniertes Code-Signing-Zertifikat als PFX (Passwort „Test123!“).</summary>
    public static (byte[] Pfx, X509Certificate2 Certificate) CreateCodeSigning(string subject = "CN=AppForge Test Signing, O=Contoso")
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.3")], false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var pfx = cert.Export(X509ContentType.Pfx, "Test123!");
        return (pfx, X509CertificateLoader.LoadPkcs12(pfx, "Test123!"));
    }
}

public static class Tooling
{
    public static string JsignJar =>
        Environment.GetEnvironmentVariable("APPFORGE_JSIGN") is { Length: > 0 } p ? p : "/opt/jsign/jsign.jar";

    public static string Pwsh =>
        Environment.GetEnvironmentVariable("APPFORGE_PWSH") is { Length: > 0 } p ? p : (System.IO.File.Exists("/opt/pwsh/pwsh") ? "/opt/pwsh/pwsh" : "pwsh");
}

public sealed class InMemorySecretStore : AppForge.Core.Abstractions.ISecretStore
{
    public Dictionary<string, string> Values { get; } = [];

    public Task<string?> GetAsync(string name, CancellationToken ct = default) => Task.FromResult(Values.TryGetValue(name, out var v) ? v : null);

    public Task SetAsync(string name, string value, CancellationToken ct = default)
    {
        Values[name] = value;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken ct = default)
    {
        Values.Remove(name);
        return Task.CompletedTask;
    }
}
