using System.Security.Cryptography;
using System.Text;
using AppForge.Core.Domain;

namespace AppForge.Core.Audit;

/// <summary>
/// Audit-Log als Hash-Kette: Hash = SHA-256(PrevHash | At | Actor | Action | SubjectType | SubjectId | Data).
/// Jede nachträgliche Änderung oder Löschung bricht die Kette und ist bei <see cref="Verify"/> sichtbar.
/// </summary>
public static class AuditChain
{
    public const string Genesis = "GENESIS";

    public static string ComputeHash(AuditEntryEntity entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var canonical = string.Join(
            '\u001f',
            entry.PrevHash,
            entry.TenantId.ToString("D"),
            entry.At.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            entry.Actor,
            entry.Action,
            entry.SubjectType,
            entry.SubjectId,
            entry.Data);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static AuditEntryEntity Seal(AuditEntryEntity entry, string? previousHash)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.PrevHash = previousHash ?? Genesis;
        entry.Hash = ComputeHash(entry);
        return entry;
    }

    /// <summary>Prüft die Kette; liefert die ID des ersten defekten Eintrags oder null.</summary>
    public static long? Verify(IEnumerable<AuditEntryEntity> orderedEntries)
    {
        ArgumentNullException.ThrowIfNull(orderedEntries);
        var expectedPrev = Genesis;
        foreach (var e in orderedEntries)
        {
            if (e.PrevHash != expectedPrev || e.Hash != ComputeHash(e))
            {
                return e.Id;
            }

            expectedPrev = e.Hash;
        }

        return null;
    }
}
