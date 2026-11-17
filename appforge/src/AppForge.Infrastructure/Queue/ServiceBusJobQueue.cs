using System.Text.Json;
using AppForge.Core.Abstractions;
using AppForge.Core.Domain;
using AppForge.Infrastructure.Data;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;

namespace AppForge.Infrastructure.Queue;

/// <summary>
/// Warteschlange über Azure Service Bus (Cloud). Die Nachricht trägt nur Job-ID und Typ; der Zustand
/// liegt in der Job-Tabelle. Der KEDA-Scaler der Container-Apps-Jobs skaliert nach Queue-Länge.
/// </summary>
public sealed class ServiceBusJobQueue(
    IDbContextFactory<AppForgeDbContext> factory,
    ServiceBusClient client,
    string queueName,
    TimeProvider time) : IJobQueue, IAsyncDisposable
{
    private readonly ServiceBusSender _sender = client.CreateSender(queueName);
    private readonly ServiceBusReceiver _receiver = client.CreateReceiver(queueName, new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock });

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

        var job = new JobEntity { Type = type, PayloadJson = payloadJson, IdempotencyKey = idempotencyKey, EnqueuedAt = time.GetUtcNow(), TenantId = tenantId ?? TenantEntity.DefaultTenant };
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var message = new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(new { job.Id, job.Type }))
        {
            MessageId = idempotencyKey ?? job.Id.ToString("N"),
            Subject = type,
            ContentType = "application/json",
        };
        await _sender.SendMessageAsync(message, ct).ConfigureAwait(false);
        return job.Id;
    }

    public async Task<JobLease?> ReceiveAsync(string workerId, TimeSpan leaseDuration, CancellationToken ct = default)
    {
        var message = await _receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
        if (message is null)
        {
            return null;
        }

        var envelope = JsonSerializer.Deserialize<Envelope>(message.Body.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var job = envelope is null ? null : await db.Jobs.FirstOrDefaultAsync(j => j.Id == envelope.Id, ct).ConfigureAwait(false);
        if (job is null || job.State is JobState.Succeeded or JobState.Failed or JobState.DeadLettered)
        {
            await _receiver.CompleteMessageAsync(message, ct).ConfigureAwait(false);
            return null;
        }

        job.State = JobState.Leased;
        job.LeasedBy = workerId;
        job.LeaseUntil = time.GetUtcNow().Add(leaseDuration);
        job.Attempts = message.DeliveryCount;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new JobLease(job.Id, job.Type, job.PayloadJson, job.Attempts, message);
    }

    public async Task CompleteAsync(JobLease lease, string? resultJson, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        await UpdateAsync(lease.JobId, JobState.Succeeded, null, resultJson, ct).ConfigureAwait(false);
        if (lease.TransportState is ServiceBusReceivedMessage message)
        {
            await _receiver.CompleteMessageAsync(message, ct).ConfigureAwait(false);
        }
    }

    public async Task FailAsync(JobLease lease, string error, bool retry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var final = !retry || lease.Attempt >= DbJobQueue.MaxAttempts;
        await UpdateAsync(lease.JobId, final ? (retry ? JobState.DeadLettered : JobState.Failed) : JobState.Queued, error, null, ct).ConfigureAwait(false);
        if (lease.TransportState is ServiceBusReceivedMessage message)
        {
            if (final)
            {
                await _receiver.DeadLetterMessageAsync(message, "JobFailed", error.Length > 1000 ? error[..1000] : error, ct).ConfigureAwait(false);
            }
            else
            {
                await _receiver.AbandonMessageAsync(message, cancellationToken: ct).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync().ConfigureAwait(false);
        await _receiver.DisposeAsync().ConfigureAwait(false);
    }

    private async Task UpdateAsync(Guid id, JobState state, string? error, string? result, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var job = await db.Jobs.FirstAsync(j => j.Id == id, ct).ConfigureAwait(false);
        job.State = state;
        job.Error = error ?? job.Error;
        job.ResultJson = result ?? job.ResultJson;
        job.LeaseUntil = null;
        if (state != JobState.Queued)
        {
            job.CompletedAt = time.GetUtcNow();
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private sealed record Envelope(Guid Id, string Type);
}
