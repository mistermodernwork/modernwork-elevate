namespace ModernWorkElevate.Tests.Services;

using ModernWorkElevate.Core.Models;
using ModernWorkElevate.Core.Services;

public sealed class ApprovalOutcomeClassifierTests
{
    [Theory]
    [InlineData(ActivationStatus.Provisioned)]
    [InlineData(ActivationStatus.Granted)]
    [InlineData(ActivationStatus.PendingScheduleCreation)]
    public void Classify_StatusAfterApproval_IsApproved(ActivationStatus status)
        => Assert.Equal(ApprovalOutcome.Approved, ApprovalOutcomeClassifier.Classify(status, isActiveNow: false, isWithinGracePeriod: false));

    [Theory]
    [InlineData(ActivationStatus.Denied, ApprovalOutcome.Denied)]
    [InlineData(ActivationStatus.Canceled, ApprovalOutcome.Canceled)]
    [InlineData(ActivationStatus.TimedOut, ApprovalOutcome.Expired)]
    [InlineData(ActivationStatus.Failed, ApprovalOutcome.Failed)]
    public void Classify_ClosedWithoutApproval_NamesTheReason(ActivationStatus status, ApprovalOutcome expected)
        => Assert.Equal(expected, ApprovalOutcomeClassifier.Classify(status, isActiveNow: false, isWithinGracePeriod: false));

    [Fact]
    public void Classify_StillPendingApproval_StaysPending()
        => Assert.Equal(
            ApprovalOutcome.StillPending,
            ApprovalOutcomeClassifier.Classify(ActivationStatus.PendingApproval, isActiveNow: false, isWithinGracePeriod: false));

    [Fact]
    public void Classify_StatusUnreadableButRoleIsActive_IsApproved()
    {
        // The request could not be read back, but the role is live: nothing else
        // than an approval makes an approval-gated role active.
        Assert.Equal(ApprovalOutcome.Approved, ApprovalOutcomeClassifier.Classify(null, isActiveNow: true, isWithinGracePeriod: false));
        Assert.Equal(ApprovalOutcome.Approved, ApprovalOutcomeClassifier.Classify(ActivationStatus.Unknown, isActiveNow: true, isWithinGracePeriod: false));
    }

    [Fact]
    public void Classify_JustRequestedAndNotYetReadable_StaysPending()
    {
        // Graph's read side lags the POST by seconds: a request made a moment ago
        // that neither lists nor reads back has not been closed — it is not there yet.
        Assert.Equal(ApprovalOutcome.StillPending, ApprovalOutcomeClassifier.Classify(null, isActiveNow: false, isWithinGracePeriod: true));
    }

    [Fact]
    public void Classify_GoneAndUnreadableAfterTheGracePeriod_IsClosed()
        => Assert.Equal(ApprovalOutcome.Closed, ApprovalOutcomeClassifier.Classify(null, isActiveNow: false, isWithinGracePeriod: false));

    [Fact]
    public void Classify_DeniedEvenThoughARoleOfTheSameNameIsActive_StaysDenied()
    {
        // The same role can be active through another path; the request's own
        // status is the better witness when there is one.
        Assert.Equal(ApprovalOutcome.Denied, ApprovalOutcomeClassifier.Classify(ActivationStatus.Denied, isActiveNow: true, isWithinGracePeriod: false));
    }
}
