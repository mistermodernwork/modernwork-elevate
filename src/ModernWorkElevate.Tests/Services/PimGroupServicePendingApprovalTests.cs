namespace ModernWorkElevate.Tests.Services;

using Microsoft.Extensions.Logging.Abstractions;
using ModernWorkElevate.Core.Models;
using ModernWorkElevate.Core.Services;
using ModernWorkElevate.Tests.TestSupport;
using Moq;

public sealed class PimGroupServicePendingApprovalTests
{
    private const string RequestId = "7a3c9b1e-2f4d-4e6a-8b0c-1d2e3f4a5b6c";

    private const string PendingRequests = """
        {
          "value": [
            {
              "id": "7a3c9b1e-2f4d-4e6a-8b0c-1d2e3f4a5b6c",
              "status": "PendingApproval",
              "action": "selfActivate",
              "accessId": "owner",
              "principalId": "user-oid-1",
              "groupId": "group-1",
              "createdDateTime": "2026-11-18T09:30:00Z",
              "scheduleInfo": { "expiration": { "type": "afterDuration", "duration": "PT1H" } }
            },
            {
              "id": "22222222-2222-2222-2222-222222222222",
              "status": "PendingApproval",
              "action": "selfDeactivate",
              "accessId": "member",
              "principalId": "user-oid-1",
              "groupId": "group-2"
            }
          ]
        }
        """;

    [Fact]
    public async Task GetPendingApprovalsAsync_ListsOwnPendingActivationsWithGroupNames()
    {
        var handler = new FakeHttpMessageHandler(FakeHttpMessageHandler.JsonResponse(PendingRequests));
        var resolver = new Mock<IGroupResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, GroupInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["group-1"] = new GroupInfo("group-1", "grp-tier0-break-glass", IsAssignableToRole: true),
            });
        var service = new PimGroupService(GraphClientTestBuilder.Build(handler), resolver.Object, NullLogger<PimGroupService>.Instance);

        var result = await service.GetPendingApprovalsAsync(TestContext.Current.CancellationToken);

        var url = Uri.UnescapeDataString(handler.Requests[0].RequestUri!.ToString());
        Assert.Contains("assignmentScheduleRequests/filterByCurrentUser(on='principal')", url, StringComparison.Ordinal);
        Assert.Contains("status eq 'PendingApproval'", url, StringComparison.Ordinal);

        var pending = Assert.Single(result);
        Assert.Equal(PimResourceKind.GroupOwnership, pending.Kind);
        Assert.Equal("grp-tier0-break-glass", pending.DisplayName);
        Assert.Equal("group-1", pending.ResourceId);
        Assert.Equal("group-1", pending.ScopeId);
        Assert.Equal(RequestId, pending.RequestId);
        Assert.Equal(TimeSpan.FromHours(1), pending.RequestedDuration);
    }

    [Fact]
    public async Task GetRequestStatusAsync_ReadsTheRequestById()
    {
        var handler = new FakeHttpMessageHandler(FakeHttpMessageHandler.JsonResponse(
            $$"""{ "value": [ { "id": "{{RequestId}}", "status": "Denied", "action": "selfActivate" } ] }"""));
        var service = new PimGroupService(GraphClientTestBuilder.Build(handler), Mock.Of<IGroupResolver>(), NullLogger<PimGroupService>.Instance);

        var status = await service.GetRequestStatusAsync(RequestId, TestContext.Current.CancellationToken);

        Assert.Equal(ActivationStatus.Denied, status);
        Assert.Contains($"id eq '{RequestId}'", Uri.UnescapeDataString(handler.Requests[0].RequestUri!.ToString()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetRequestStatusAsync_IdThatIsNotAGuid_IsNeverSentToGraph()
    {
        var handler = new FakeHttpMessageHandler(FakeHttpMessageHandler.JsonResponse("""{ "value": [] }"""));
        var service = new PimGroupService(GraphClientTestBuilder.Build(handler), Mock.Of<IGroupResolver>(), NullLogger<PimGroupService>.Instance);

        Assert.Null(await service.GetRequestStatusAsync("x' or 1 eq 1", TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.RequestCount);
    }
}
