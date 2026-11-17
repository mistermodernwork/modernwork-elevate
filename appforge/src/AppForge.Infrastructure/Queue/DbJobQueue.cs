using AppForge.Core.Abstractions;
using AppForge.Core.Domain;
using AppForge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AppForge.Infrastructure.Queue;

/// <summary>
/// Warteschlange als Datenbanktabelle (On-Prem). Leasing per bedingtem UPDATE: nur wer die Zeile
/// tatsächlich umschreibt, bekommt den Job – mehrere Worker sind damit sicher.
/// </summary>
public sealed class DbJobQueue(IDbContextFactory<AppForgeDbContext> factory, TimeProvider time) : IJobQueue
{
    public const int MaxAttempts = 3;

    public async Task<Guid> EnqueueAsync(string type, string payloadJson, string? idempotencyKey = null, Guid? tenantId = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (idempotencyKey is not null)
        {
            var existing = await db.Jobs.AsNoTracking().Where(j => j.IdempotencyKey == idempotencyKey).Select(j => (Guid?)j.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (existing is { } id)
            {
                return id;
            }
        }

        var job = new JobEntity
        {
            Type = type,
            PayloadJson = payloadJson,
            IdempotencyKey = idempotencyKey,
            EnqueuedAt = time.GetUtcNow(),
            TenantId = tenantId ?? TenantEntity.DefaultTenant,
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return job.Id;
    }

    public async Task<JobLease?> ReceiveAsync(string workerId, TimeSpan leaseDuration, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var now = time.GetUtcNow();
        var candidates = await db.Jobs.AsNoTracking()
            .Where(j => j.State == JobState.Queued || (j.State == JobState.Leased && j.LeaseUntil < now))
            .OrderBy(j => j.EnqueuedAt)
            .Select(j => new { j.Id, j.Type, j.PayloadJson, j.Attempts })
            .Take(5)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var c in candidates)
        {
            var until = now.Add(leaseDuration);
            var updated = await db.Jobs
                .Where(j => j.Id == c.Id && j.Attempts == c.Attempts && (j.State == JobState.Queued || (j.State == JobState.Leased && j.LeaseUntil < now)))
                .ExecuteUpdateAsync(
                    s => s.SetProperty(j => j.State, JobState.Leased)
                        .SetProperty(j => j.LeasedBy, workerId)
                        .SetProperty(j => j.LeaseUntil, until)
                        .SetProperty(j => j.Attempts, c.Attempts + 1),
                    ct).ConfigureAwait(false);
            if (updated == 1)
            {
                if (c.Attempts + 1 > MaxAttempts)
                {
                    await db.Jobs.Where(j => j.Id == c.Id).ExecuteUpdateAsync(
                        s => s.SetProperty(j => j.State, JobState.DeadLettered).SetProperty(j => j.Error, "Maximale Anzahl Versuche überschritten."), ct).ConfigureAwait(false);
                    continue;
                }

                return new JobLease(c.Id, c.Type, c.PayloadJson, c.Attempts + 1);
            }
        }

        return null;
    }

    public async Task CompleteAsync(JobLease lease, string? resultJson, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var now = time.GetUtcNow();
        await db.Jobs.Where(j => j.Id == lease.JobId).ExecuteUpdateAsync(
            s => s.SetProperty(j => j.State, JobState.Succeeded)
                .SetProperty(j => j.CompletedAt, now)
                .SetProperty(j => j.ResultJson, resultJson)
                .SetProperty(j => j.LeaseUntil, (DateTimeOffset?)null),
            ct).ConfigureAwait(false);
    }

    public async Task FailAsync(JobLease lease, string error, bool retry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var now = time.GetUtcNow();
        var final = !retry || lease.Attempt >= MaxAttempts;
        await db.Jobs.Where(j => j.Id == lease.JobId).ExecuteUpdateAsync(
            s => s.SetProperty(j => j.State, final ? (retry ? JobState.DeadLettered : JobState.Failed) : JobState.Queued)
                .SetProperty(j => j.Error, error)
                .SetProperty(j => j.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(j => j.CompletedAt, final ? now : null),
            ct).ConfigureAwait(false);
    }
}
