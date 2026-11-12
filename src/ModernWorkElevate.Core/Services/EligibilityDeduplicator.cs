namespace ModernWorkElevate.Core.Services;

using ModernWorkElevate.Core.Models;

/// <summary>
/// Collapses eligibilities that point at the same activatable thing — same kind, same
/// role or group, same scope — into one row.
/// </summary>
/// <remarks>
/// Field case 2026-10-26: one PIM group appeared three times in the list.
/// <c>filterByCurrentUser(on='principal')</c> returns one schedule instance per eligibility
/// path: a direct eligibility and one per group the user is eligible through, and split
/// schedules (e.g. a renewed eligibility) as separate instances. Activating any of them grants
/// the same access, so the user sees one row. The kept row prefers the direct eligibility
/// (principal = the signed-in user — the one a self-activation is filed for), then the one
/// that runs longest (no end date first).
/// </remarks>
public static class EligibilityDeduplicator
{
    /// <summary>
    /// Returns <paramref name="eligibilities"/> with duplicates collapsed, in the order each
    /// item first appeared.
    /// </summary>
    /// <param name="eligibilities">The raw list from all surfaces of one account.</param>
    /// <param name="userObjectId">The signed-in user's object id.</param>
    /// <param name="collapsed">How many rows were dropped.</param>
    /// <returns>The de-duplicated list.</returns>
    public static IReadOnlyList<PimEligibility> Collapse(
        IReadOnlyList<PimEligibility> eligibilities,
        string? userObjectId,
        out int collapsed)
    {
        ArgumentNullException.ThrowIfNull(eligibilities);

        var order = new List<(PimResourceKind, string, string)>();
        var best = new Dictionary<(PimResourceKind, string, string), PimEligibility>();
        foreach (var item in eligibilities)
        {
            var key = (item.Kind, item.ResourceId.ToUpperInvariant(), item.ScopeId.ToUpperInvariant());
            if (!best.TryGetValue(key, out var current))
            {
                best[key] = item;
                order.Add(key);
            }
            else if (IsBetter(item, current, userObjectId))
            {
                best[key] = item;
            }
        }

        collapsed = eligibilities.Count - order.Count;
        return [.. order.Select(key => best[key])];
    }

    /// <summary>
    /// Same collapse for active assignments. The kept row is the direct assignment when there
    /// is one: deactivation is filed for the signed-in user, so that is the instance whose
    /// schedule a "deactivate" must end. Otherwise the one that runs longest.
    /// </summary>
    /// <param name="assignments">The raw list from all surfaces of one account.</param>
    /// <param name="userObjectId">The signed-in user's object id.</param>
    /// <returns>The de-duplicated list, in first-seen order.</returns>
    public static IReadOnlyList<ActiveAssignment> CollapseActive(
        IReadOnlyList<ActiveAssignment> assignments,
        string? userObjectId)
    {
        ArgumentNullException.ThrowIfNull(assignments);

        var order = new List<(PimResourceKind, string, string)>();
        var best = new Dictionary<(PimResourceKind, string, string), ActiveAssignment>();
        foreach (var item in assignments)
        {
            var key = (item.Kind, item.ResourceId.ToUpperInvariant(), item.ScopeId.ToUpperInvariant());
            if (!best.TryGetValue(key, out var current))
            {
                best[key] = item;
                order.Add(key);
                continue;
            }

            var candidateDirect = IsDirect(item.PrincipalId, userObjectId);
            var currentDirect = IsDirect(current.PrincipalId, userObjectId);
            if (candidateDirect != currentDirect
                ? candidateDirect
                : (item.EndDateTime ?? DateTimeOffset.MaxValue) > (current.EndDateTime ?? DateTimeOffset.MaxValue))
            {
                best[key] = item;
            }
        }

        return [.. order.Select(key => best[key])];
    }

    private static bool IsBetter(PimEligibility candidate, PimEligibility current, string? userObjectId)
    {
        var candidateDirect = IsDirect(candidate.PrincipalId, userObjectId);
        var currentDirect = IsDirect(current.PrincipalId, userObjectId);
        if (candidateDirect != currentDirect)
        {
            return candidateDirect;
        }

        // Longest-running wins; no end date means permanent eligibility.
        return (candidate.EndDateTime ?? DateTimeOffset.MaxValue) > (current.EndDateTime ?? DateTimeOffset.MaxValue);
    }

    private static bool IsDirect(string principalId, string? userObjectId)
        => !string.IsNullOrEmpty(userObjectId)
            && string.Equals(principalId, userObjectId, StringComparison.OrdinalIgnoreCase);
}
