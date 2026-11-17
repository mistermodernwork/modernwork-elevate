using System.Text;
using AppForge.Core.Domain;
using AppForge.Infrastructure.Audit;
using AppForge.Infrastructure.Queue;
using AppForge.Infrastructure.Secrets;
using AppForge.Infrastructure.Storage;
using AppForge.Tests.TestSupport;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace AppForge.Tests.Infrastructure;

public class DbJobQueueTests : IDisposable
{
    private readonly SqliteDb _db = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 17, 8, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Enqueue_receive_complete()
    {
        var q = new DbJobQueue(_db.Factory, _time);
        var id = await q.EnqueueAsync("ping", "{}");
        var lease = await q.ReceiveAsync("w1", TimeSpan.FromMinutes(5));
        Assert.NotNull(lease);
        Assert.Equal(id, lease.JobId);
        Assert.Null(await q.ReceiveAsync("w2", TimeSpan.FromMinutes(5)));
        await q.CompleteAsync(lease, "{\"ok\":true}");
        using var db = _db.Create();
        var job = await db.Jobs.SingleAsync();
        Assert.Equal(JobState.Succeeded, job.State);
        Assert.Equal("{\"ok\":true}", job.ResultJson);
    }

    [Fact]
    public async Task Idempotency_key_prevents_duplicates()
    {
        var q = new DbJobQueue(_db.Factory, _time);
        var a = await q.EnqueueAsync("build", "{}", "build:7zip:24.08");
        var b = await q.EnqueueAsync("build", "{}", "build:7zip:24.08");
        Assert.Equal(a, b);
        using var db = _db.Create();
        Assert.Equal(1, await db.Jobs.CountAsync());
    }

    [Fact]
    public async Task Expired_lease_is_taken_over_and_dead_lettered_after_max_attempts()
    {
        var q = new DbJobQueue(_db.Factory, _time);
        await q.EnqueueAsync("build", "{}");
        for (var i = 1; i <= DbJobQueue.MaxAttempts; i++)
        {
            var lease = await q.ReceiveAsync("w" + i, TimeSpan.FromMinutes(1));
            Assert.NotNull(lease);
            Assert.Equal(i, lease.Attempt);
            _time.Now = _time.Now.AddMinutes(2);
        }

        Assert.Null(await q.ReceiveAsync("w9", TimeSpan.FromMinutes(1)));
        using var db = _db.Create();
        Assert.Equal(JobState.DeadLettered, (await db.Jobs.SingleAsync()).State);
    }

    [Fact]
    public async Task Fail_with_retry_requeues_and_without_retry_fails()
    {
        var q = new DbJobQueue(_db.Factory, _time);
        await q.EnqueueAsync("x", "{}");
        var lease = await q.ReceiveAsync("w", TimeSpan.FromMinutes(1));
        await q.FailAsync(lease!, "transient", retry: true);
        var again = await q.ReceiveAsync("w", TimeSpan.FromMinutes(1));
        Assert.NotNull(again);
        await q.FailAsync(again, "fatal", retry: false);
        using var db = _db.Create();
        var job = await db.Jobs.SingleAsync();
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("fatal", job.Error);
    }

    [Fact]
    public async Task Concurrent_workers_never_get_the_same_job()
    {
        var q = new DbJobQueue(_db.Factory, _time);
        for (var i = 0; i < 10; i++)
        {
            await q.EnqueueAsync("x", "{}");
        }

        var leases = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => q.ReceiveAsync("w" + i, TimeSpan.FromMinutes(5))));
        var ids = leases.Where(l => l is not null).Select(l => l!.JobId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}

public class AuditLogTests : IDisposable
{
    private readonly SqliteDb _db = new();

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Writes_chain_and_detects_tampering()
    {
        var log = new DbAuditLog(_db.Factory, TimeProvider.System);
        await log.WriteAsync("alice", "sign.submitted", "SigningJob", "1", new { files = 2 });
        await log.WriteAsync("bob", "approval.approved", "ApprovalRequest", "2");
        await log.WriteAsync("worker", "sign.completed", "SigningJob", "1");
        var ok = await log.VerifyAsync();
        Assert.True(ok.Valid);
        Assert.Equal(3, ok.Count);

        using (var db = _db.Create())
        {
            var e = await db.AuditEntries.OrderBy(a => a.Id).Skip(1).FirstAsync();
            e.Actor = "mallory";
            await db.SaveChangesAsync();
        }

        var broken = await log.VerifyAsync();
        Assert.False(broken.Valid);
        Assert.Equal(2, broken.BrokenAt);
    }

    [Fact]
    public async Task Parallel_writes_keep_a_single_chain()
    {
        var log = new DbAuditLog(_db.Factory, TimeProvider.System);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => log.WriteAsync("u", "a", "t", i.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        var r = await log.VerifyAsync();
        Assert.True(r.Valid);
        Assert.Equal(20, r.Count);
    }
}

public class StorageTests
{
    [Fact]
    public async Task File_store_is_immutable_and_hashes()
    {
        using var dir = new TempDir();
        var store = new FileSystemArtifactStore(dir.Path);
        var a = await store.PutAsync("builds/1/a.txt", new MemoryStream(Encoding.UTF8.GetBytes("hallo")));
        Assert.Equal("D3751D33F9CD5049C4AF2B462735457E4D3BAF130BCBB87F389E349FBAEB20B9", a.Sha256);
        Assert.Equal(5, a.Size);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PutAsync("builds/1/a.txt", new MemoryStream([1])));
        Assert.True(await store.ExistsAsync("builds/1/a.txt"));
        await using (var s = await store.OpenReadAsync("builds/1/a.txt"))
        {
            Assert.Equal("hallo", await new StreamReader(s).ReadToEndAsync());
        }

        var keys = new List<string>();
        await foreach (var k in store.ListAsync("builds"))
        {
            keys.Add(k);
        }

        Assert.Equal(["builds/1/a.txt"], keys);
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("/etc/passwd")]
    [InlineData("a/../../b")]
    [InlineData("C:x")]
    [InlineData("")]
    public void File_store_rejects_path_traversal(string key)
    {
        using var dir = new TempDir();
        Assert.Throws<ArgumentException>(() => new FileSystemArtifactStore(dir.Path).Resolve(key));
    }

    [Fact]
    public async Task Data_protection_secret_store_encrypts_at_rest()
    {
        using var dir = new TempDir();
        var provider = DataProtectionProvider.Create(new DirectoryInfo(dir.File("keys")));
        var store = new DataProtectionSecretStore(provider, dir.File("secrets"));
        await store.SetAsync("github-token", "ghp_supersecret");
        Assert.Equal("ghp_supersecret", await store.GetAsync("github-token"));
        Assert.DoesNotContain("ghp_supersecret", await File.ReadAllTextAsync(dir.File("secrets/secrets.json")), StringComparison.Ordinal);
        await store.DeleteAsync("github-token");
        Assert.Null(await store.GetAsync("github-token"));
    }

    [Fact]
    public void Key_vault_names_are_normalized()
    {
        Assert.Equal("publish-github-token", KeyVaultSecretStore.Normalize("publish:github_token"));
    }
}
