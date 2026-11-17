using System.Text.Json;
using AppForge.Core.Abstractions;
using AppForge.Core.Audit;
using AppForge.Core.Domain;
using AppForge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AppForge.Infrastructure.Audit;

/// <summary>
/// Append-only Audit-Log mit Hash-Kette. Innerhalb eines Prozesses serialisiert; zwischen Prozessen
/// verhindert der eindeutige Index auf <c>PrevHash</c> eine Verzweigung der Kette (Konflikt → erneut versuchen).
/// </summary>
public sealed class DbAuditLog(IDbContextFactory<AppForgeDbContext> factory, TimeProvider time) : IAuditLog
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task WriteAsync(string actor, string action, string subjectType, string subjectId, object? data = null, CancellationToken ct = default)
    {
        var json = data is null ? "{}" : data as string ?? JsonSerializer.Serialize(data);
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
                var last = await db.AuditEntries.AsNoTracking().OrderByDescending(a => a.Id).Select(a => a.Hash).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                var entry = AuditChain.Seal(
                    new AuditEntryEntity
                    {
                        At = time.GetUtcNow(),
                        Actor = actor,
                        Action = action,
                        SubjectType = subjectType,
                        SubjectId = subjectId,
                        Data = json,
                    },
                    last);
                db.AuditEntries.Add(entry);
                try
                {
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    return;
                }
                catch (DbUpdateException) when (attempt < 4)
                {
                    // Ein anderer Prozess hat gleichzeitig geschrieben – Kettenende neu lesen.
                }
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<(bool Valid, long? BrokenAt, int Count)> VerifyAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entries = await db.AuditEntries.AsNoTracking().OrderBy(a => a.Id).ToListAsync(ct).ConfigureAwait(false);
        var broken = AuditChain.Verify(entries);
        return (broken is null, broken, entries.Count);
    }
}
