namespace ModernWorkElevate.Tests.Services;

using ModernWorkElevate.Core.Models;
using ModernWorkElevate.Core.Services;

public sealed class EligibilityDeduplicatorTests
{
    private const string User = "user-oid";

    [Fact]
    public void Collapse_SameGroupThreeTimes_KeepsOneRowPreferringTheDirectEligibility()
    {
        // Field case 2026-10-26: one PIM group listed three times (direct + via groups).
        PimEligibility[] raw =
        [
            Group("grp-1", principal: "nesting-group-a", end: null),
            Group("grp-1", principal: User, end: DateTimeOffset.UtcNow.AddDays(30)),
            Group("GRP-1", principal: "nesting-group-b", end: null),
        ];

        var result = EligibilityDeduplicator.Collapse(raw, User, out var collapsed);

        var row = Assert.Single(result);
        Assert.Equal(User, row.PrincipalId);
        Assert.Equal(2, collapsed);
    }

    [Fact]
    public void Collapse_WithoutADirectOne_KeepsTheLongestRunning()
    {
        var soon = DateTimeOffset.UtcNow.AddDays(1);
        PimEligibility[] raw =
        [
            Group("grp-1", principal: "g-a", end: soon),
            Group("grp-1", principal: "g-b", end: null),
        ];

        var row = Assert.Single(EligibilityDeduplicator.Collapse(raw, User, out _));

        Assert.Null(row.EndDateTime);
    }

    [Fact]
    public void Collapse_DifferentScopeOrKind_StaysSeparate()
    {
        PimEligibility[] raw =
        [
            new(PimResourceKind.DirectoryRole, "Intune Administrator", "role-intune", "/", User, null, false),
            new(PimResourceKind.DirectoryRole, "Intune Administrator", "role-intune", "/administrativeUnits/au-1", User, null, false),
            Group("grp-1", principal: User, end: null),
            new(PimResourceKind.GroupOwnership, "grp-1", "grp-1", "grp-1", User, null, false),
        ];

        var result = EligibilityDeduplicator.Collapse(raw, User, out var collapsed);

        Assert.Equal(4, result.Count);
        Assert.Equal(0, collapsed);
        Assert.Equal("/", result[0].ScopeId);
    }

    [Fact]
    public void CollapseActive_SameRoleViaSeveralPaths_KeepsTheDirectAssignment()
    {
        ActiveAssignment[] raw =
        [
            Active("via-group", "sched-group"),
            Active(User, "sched-direct"),
            Active("via-other-group", "sched-other"),
        ];

        var row = Assert.Single(EligibilityDeduplicator.CollapseActive(raw, User));

        Assert.Equal("sched-direct", row.AssignmentScheduleId);
    }

    private static ActiveAssignment Active(string principal, string scheduleId)
        => new(PimResourceKind.DirectoryRole, "Intune Administrator", "role-intune", "/", principal, null, DateTimeOffset.UtcNow.AddHours(1), scheduleId);

    private static PimEligibility Group(string groupId, string principal, DateTimeOffset? end)
        => new(PimResourceKind.GroupMembership, "AAD-AR-group", groupId, groupId, principal, end, false);
}
