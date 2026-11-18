namespace ModernWorkElevate.Tests.Services;

using Microsoft.Extensions.Logging.Abstractions;
using ModernWorkElevate.Core.Models;
using ModernWorkElevate.Core.Services;
using ModernWorkElevate.Tests.TestSupport;

public sealed class PimRoleServicePendingApprovalTests
{
    private const string RequestId = "6f9619ff-8b86-d011-b42d-00c04fc964ff";

    private const string PendingRequests = """
        {
          "value": [
            {
              "id": "6f9619ff-8b86-d011-b42d-00c04fc964ff",
              "status": "PendingApproval",
              "action": "selfActivate",
              "principalId": "user-oid-1",
              "roleDefinitionId": "role-def-ga",
              "directoryScopeId": "/",
              "createdDateTime": "2026-11-18T10:00:00Z",
              "justification": "INC-1 — never logged",
              "scheduleInfo": { "startDateTime": "2026-11-18T10:00:00Z", "expiration": { "type": "afterDuration", "duration": "PT2H" } },
              "roleDefinition": { "id": "role-def-ga", "displayName": "Global Administrator" }
            },
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "status": "PendingApproval",
              "action": "adminAssign",
              "principalId": "user-oid-1",
              "roleDefinitionId": "role-def-ua",
              "directoryScopeId": "/",
              "roleDefinition": { "id": "role-def-ua", "displayName": "User Administrator" }
            }
          ]
        }
        """;

    [Fact]
    public async Task GetPendingApprovalsAsync_ListsOwnPendingActivations()
    {
        var handler = new FakeHttpMessageHandler(FakeHttpMessageHandler.JsonResponse(PendingRequests));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler), NullLogger<PimRoleService>.Instance);

        var result = await service.GetPendingApprovalsAsync(TestContext.Current.CancellationToken);

        var url = Uri.UnescapeDataString(handler.Requests[0].RequestUri!.ToString());
        Assert.Contains("roleAssignmentScheduleRequests/filterByCurrentUser(on='principal')", url, StringComparison.Ordinal);
        Assert.Contains("status eq 'PendingApproval'", url, StringComparison.Ordinal);

        // An admin's assignment that awaits approval is not something this user asked for.
        var pending = Assert.Single(result);
        Assert.Equal(PimResourceKind.DirectoryRole, pending.Kind);
        Assert.Equal("Global Administrator", pending.DisplayName);
        Assert.Equal("role-def-ga", pending.ResourceId);
        Assert.Equal("/", pending.ScopeId);
        Assert.Equal(RequestId, pending.RequestId);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero), pending.RequestedAt);
        Assert.Equal(TimeSpan.FromHours(2), pending.RequestedDuration);
    }

    [Fact]
    public async Task GetRequestStatusAsync_ReadsTheRequestById()
    {
        var handler = new FakeHttpMessageHandler(FakeHttpMessageHandler.JsonResponse(
            $$"""{ "value": [ { "id": "{{RequestId}}", "status": "Provisioned", "action": "selfActivate" } ] }"""));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler), NullLogger<PimRoleService>.Instance);

        var status = await service.GetRequestStatusAsync(RequestId, TestContext.Current.CancellationToken);

        Assert.Equal(ActivationStatus.Provisioned, status);
        Assert.Contains($"id eq '{RequestId}'", Uri.UnescapeDataString(handler.Requests[0].RequestUri!.ToString()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetRequestStatusAsync_NotFound_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(FakeHttpMessageHandler.JsonResponse("""{ "value": [] }"""));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler), NullLogger<PimRoleService>.Instance);

        Assert.Null(await service.GetRequestStatusAsync(RequestId, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("x' or status eq 'Denied")]
    [InlineData("")]
    public async Task GetRequestStatusAsync_IdThatIsNotAGuid_IsNeverSentToGraph(string requestId)
    {
        // The id goes into an OData filter; only a GUID may get there.
        var handler = new FakeHttpMessageHandler(FakeHttpMessageHandler.JsonResponse("""{ "value": [] }"""));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler), NullLogger<PimRoleService>.Instance);

        Assert.Null(await service.GetRequestStatusAsync(requestId, TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.RequestCount);
    }
}
