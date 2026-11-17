using AppForge.Core.Approval;
using AppForge.Core.Domain;

namespace AppForge.Tests.Core;

public class ApprovalWorkflowTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private static Approver P(string id, params string[] roles) => new(id, roles);

    [Fact]
    public void Level0_is_approved_immediately()
    {
        Assert.Equal(ApprovalState.Approved, new ApprovalWorkflow(0, "eng", Monday).State);
    }

    [Fact]
    public void Submitter_can_never_approve()
    {
        var wf = new ApprovalWorkflow(1, "alice", Monday);
        var r = wf.Approve(P("alice", AppRoles.Packager), "ok", Monday);
        Assert.False(r.Accepted);
        Assert.Equal(ApprovalState.Pending, wf.State);
    }

    [Fact]
    public void Submitter_check_is_case_insensitive()
    {
        var wf = new ApprovalWorkflow(1, "Alice@Contoso.com", Monday);
        Assert.False(wf.Approve(P("alice@contoso.com", AppRoles.Packager), "ok", Monday).Accepted);
    }

    [Fact]
    public void Service_principal_can_never_approve()
    {
        var wf = new ApprovalWorkflow(1, "alice", Monday);
        Assert.False(wf.Approve(new Approver("pipeline", [AppRoles.Packager], IsServicePrincipal: true), "ok", Monday).Accepted);
        Assert.False(wf.Approve(P("logicapp", AppRoles.Automation, AppRoles.Approver), "ok", Monday).Accepted);
    }

    [Fact]
    public void Level1_needs_one_app_owner_or_packager()
    {
        var wf = new ApprovalWorkflow(1, "alice", Monday);
        Assert.False(wf.Approve(P("eve", AppRoles.Engineer), "ok", Monday).Accepted);
        Assert.True(wf.Approve(P("bob", AppRoles.Approver), "ok", Monday).Accepted);
        Assert.Equal(ApprovalState.Approved, wf.State);
    }

    [Fact]
    public void Level2_needs_packager_and_security_as_different_people()
    {
        var wf = new ApprovalWorkflow(2, "alice", Monday);
        Assert.True(wf.Approve(P("bob", AppRoles.Packager, AppRoles.Security), "ok", Monday).Accepted);
        Assert.Equal(ApprovalState.Pending, wf.State);

        // Dieselbe Person zählt nur einmal.
        Assert.False(wf.Approve(P("bob", AppRoles.Packager, AppRoles.Security), "nochmal", Monday).Accepted);

        Assert.True(wf.Approve(P("carol", AppRoles.Security), "ok", Monday).Accepted);
        Assert.Equal(ApprovalState.Approved, wf.State);
    }

    [Fact]
    public void Level2_second_packager_does_not_satisfy_security_slot()
    {
        var wf = new ApprovalWorkflow(2, "alice", Monday);
        Assert.True(wf.Approve(P("bob", AppRoles.Packager), "ok", Monday).Accepted);
        var second = wf.Approve(P("dave", AppRoles.Packager), "ok", Monday);
        Assert.False(second.Accepted);
        Assert.Equal(ApprovalState.Pending, wf.State);
        Assert.Single(wf.OpenSlots());
        Assert.Contains(AppRoles.Security, wf.OpenSlots()[0]);
    }

    [Fact]
    public void Level2_matching_assigns_dual_role_person_to_free_slot()
    {
        var wf = new ApprovalWorkflow(2, "alice", Monday);
        Assert.True(wf.Approve(P("carol", AppRoles.Security), "ok", Monday).Accepted);
        Assert.True(wf.Approve(P("bob", AppRoles.Packager, AppRoles.Security), "ok", Monday).Accepted);
        Assert.Equal(ApprovalState.Approved, wf.State);
    }

    [Fact]
    public void Level3_needs_security_and_change_board_or_platform_owner()
    {
        var wf = new ApprovalWorkflow(3, "alice", Monday);
        Assert.False(wf.Approve(P("bob", AppRoles.Packager), "ok", Monday).Accepted);
        Assert.True(wf.Approve(P("carol", AppRoles.Security), "ok", Monday).Accepted);
        Assert.True(wf.Approve(P("dan", AppRoles.PlatformAdmin), "ok", Monday).Accepted);
        Assert.Equal(ApprovalState.Approved, wf.State);
    }

    [Fact]
    public void Rejection_requires_reason_and_is_final()
    {
        var wf = new ApprovalWorkflow(2, "alice", Monday);
        Assert.False(wf.Reject(P("bob", AppRoles.Packager), " ", Monday).Accepted);
        Assert.True(wf.Reject(P("bob", AppRoles.Packager), "Detection falsch", Monday).Accepted);
        Assert.Equal(ApprovalState.Rejected, wf.State);
        Assert.False(wf.Approve(P("carol", AppRoles.Security), "ok", Monday).Accepted);
    }

    [Fact]
    public void Emergency_path_only_level2_only_security_and_requires_confirmation()
    {
        Assert.False(new ApprovalWorkflow(1, "alice", Monday).ApproveEmergency(P("carol", AppRoles.Security), "CVE", Monday).Accepted);
        Assert.False(new ApprovalWorkflow(3, "alice", Monday).ApproveEmergency(P("carol", AppRoles.Security), "CVE", Monday).Accepted);

        var wf = new ApprovalWorkflow(2, "alice", Monday);
        Assert.False(wf.ApproveEmergency(P("bob", AppRoles.Packager), "CVE", Monday).Accepted);
        Assert.False(wf.ApproveEmergency(P("carol", AppRoles.Security), string.Empty, Monday).Accepted);
        Assert.True(wf.ApproveEmergency(P("carol", AppRoles.Security), "CVE-2026-1234 aktiv ausgenutzt", Monday).Accepted);
        Assert.Equal(ApprovalState.Approved, wf.State);
        Assert.True(wf.Emergency);
        Assert.False(wf.EmergencyConfirmed);

        // Frist: 2 Werktage → Mittwoch.
        Assert.Equal(Monday.AddDays(2), wf.EmergencyConfirmDueAt);
        Assert.False(wf.IsEmergencyConfirmationOverdue(Monday.AddDays(1)));
        Assert.True(wf.IsEmergencyConfirmationOverdue(Monday.AddDays(3)));

        // Bestätigung: andere Person mit Packager-Rolle.
        Assert.False(wf.Approve(P("carol", AppRoles.Security, AppRoles.Packager), "selbst", Monday.AddDays(1)).Accepted);
        Assert.False(wf.Approve(P("erin", AppRoles.Security), "falsche Rolle", Monday.AddDays(1)).Accepted);
        Assert.True(wf.Approve(P("bob", AppRoles.Packager), "bestätigt", Monday.AddDays(1)).Accepted);
        Assert.True(wf.EmergencyConfirmed);
        Assert.False(wf.IsEmergencyConfirmationOverdue(Monday.AddDays(10)));
    }

    [Fact]
    public void Due_dates_use_business_days()
    {
        var friday = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero), new ApprovalWorkflow(1, "a", friday).DueAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero), new ApprovalWorkflow(2, "a", friday).DueAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero), new ApprovalWorkflow(3, "a", friday).DueAt);
    }

    [Fact]
    public void Level3_due_date_is_next_cab_meeting_when_configured()
    {
        var options = new ApprovalOptions { CabDays = [DayOfWeek.Thursday] };
        var wf = new ApprovalWorkflow(3, "a", Monday, options);
        Assert.Equal(DayOfWeek.Thursday, wf.DueAt.DayOfWeek);
        Assert.Equal(Monday.AddDays(3), wf.DueAt);
    }

    [Fact]
    public void Holidays_are_skipped()
    {
        var options = new ApprovalOptions { Holidays = [new DateOnly(2026, 11, 17)] };
        Assert.Equal(Monday.AddDays(3), new ApprovalWorkflow(1, "a", Monday, options).DueAt);
    }

    [Fact]
    public void Overdue_and_optional_expiry()
    {
        var wf = new ApprovalWorkflow(1, "a", Monday, new ApprovalOptions { ExpireAfterOverdueBusinessDays = 1 });
        Assert.False(wf.IsOverdue(Monday.AddDays(1)));
        Assert.True(wf.IsOverdue(Monday.AddDays(2).AddHours(1)));
        Assert.False(wf.ExpireIfDue(Monday.AddDays(2).AddHours(1)));
        Assert.True(wf.ExpireIfDue(Monday.AddDays(4)));
        Assert.Equal(ApprovalState.Expired, wf.State);
    }

    [Fact]
    public void Without_expiry_option_requests_only_escalate()
    {
        var wf = new ApprovalWorkflow(1, "a", Monday);
        Assert.False(wf.ExpireIfDue(Monday.AddDays(30)));
        Assert.Equal(ApprovalState.Pending, wf.State);
    }

    [Fact]
    public void Cancel_only_by_submitter()
    {
        var wf = new ApprovalWorkflow(2, "alice", Monday);
        Assert.False(wf.Cancel("bob").Accepted);
        Assert.True(wf.Cancel("alice").Accepted);
        Assert.Equal(ApprovalState.Cancelled, wf.State);
    }

    [Fact]
    public void State_is_rebuilt_from_persisted_decisions()
    {
        var decisions = new[]
        {
            new ApprovalDecisionRecord("bob", [AppRoles.Packager], true, "ok", Monday),
            new ApprovalDecisionRecord("carol", [AppRoles.Security], true, "ok", Monday),
        };
        Assert.Equal(ApprovalState.Approved, new ApprovalWorkflow(2, "alice", Monday, existing: decisions).State);
        Assert.Equal(ApprovalState.Pending, new ApprovalWorkflow(2, "alice", Monday, existing: decisions.Take(1)).State);
        Assert.Equal(ApprovalState.Rejected, new ApprovalWorkflow(1, "alice", Monday, existing: [new ApprovalDecisionRecord("bob", [AppRoles.Packager], false, "nein", Monday)]).State);
    }

    [Fact]
    public void Invalid_level_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApprovalWorkflow(4, "a", Monday));
    }
}

public class EnforcementPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Never_audited_policy_cannot_be_enforced()
    {
        Assert.False(EnforcementPolicy.CanSwitchToEnforced(new AppControlPolicyEntity(), Now).Allowed);
    }

    [Fact]
    public void Observation_window_must_elapse()
    {
        var p = new AppControlPolicyEntity { AuditSince = Now.AddDays(-3) };
        Assert.False(EnforcementPolicy.CanSwitchToEnforced(p, Now).Allowed);
        p.AuditSince = Now.AddDays(-8);
        Assert.True(EnforcementPolicy.CanSwitchToEnforced(p, Now).Allowed);
    }

    [Fact]
    public void Recent_block_event_restarts_window()
    {
        var p = new AppControlPolicyEntity { AuditSince = Now.AddDays(-30), LastBlockEventAt = Now.AddDays(-2) };
        Assert.False(EnforcementPolicy.CanSwitchToEnforced(p, Now).Allowed);
        p.LastBlockEventAt = Now.AddDays(-8);
        Assert.True(EnforcementPolicy.CanSwitchToEnforced(p, Now).Allowed);
    }

    [Fact]
    public void Already_enforced_is_rejected()
    {
        var p = new AppControlPolicyEntity { Mode = PolicyEnforcementMode.Enforced, AuditSince = Now.AddDays(-30) };
        Assert.False(EnforcementPolicy.CanSwitchToEnforced(p, Now).Allowed);
    }

    [Fact]
    public void Ring_promotion_suggestions()
    {
        Assert.Equal(DeploymentRing.Pilot, RingPromotion.Suggest(DeploymentRing.Test, Now.AddDays(-4), false, Now));
        Assert.Null(RingPromotion.Suggest(DeploymentRing.Test, Now.AddDays(-1), false, Now));
        Assert.Null(RingPromotion.Suggest(DeploymentRing.Test, Now.AddDays(-10), true, Now));
        Assert.Equal(DeploymentRing.Production, RingPromotion.Suggest(DeploymentRing.Pilot, Now.AddDays(-8), false, Now));
        Assert.Null(RingPromotion.Suggest(DeploymentRing.Production, Now.AddDays(-80), false, Now));
    }
}
