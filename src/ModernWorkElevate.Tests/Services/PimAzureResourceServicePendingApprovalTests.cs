namespace ModernWorkElevate.Tests.Services;

using Microsoft.Extensions.Logging.Abstractions;
using ModernWorkElevate.Core.Models;
using ModernWorkElevate.Core.Services;
using ModernWorkElevate.Tests.TestSupport;

public sealed class PimAzureResourceServicePendingApprovalTests
{
    private const string UserOid = "user-oid-1";
    private const string ContributorId = "/subscriptions/sub-1/providers/Microsoft.Authorization/roleDefinitions/b24988ac-6180-42a0-ab88-20f7382dd24c";

    private const string MyRequests = """
        {
          "value": [
            {
              "name": "fea7a502-9a96-4806-a26f-eee560e52045",
              "properties": {
                "scope": "/subscriptions/sub-1",
                "roleDefinitionId": "/subscriptions/sub-1/providers/Microsoft.Authorization/roleDefinitions/b24988ac-6180-42a0-ab88-20f7382dd24c",
                "principalId": "user-oid-1",
                "requestType": "SelfActivate",
                "status": "PendingApproval",
                "createdOn": "2026-11-18T08:00:00Z",
                "scheduleInfo": { "startDateTime": "2026-11-18T08:00:00Z", "expiration": { "type": "AfterDuration", "duration": "PT4H" } },
                "expandedProperties": {
                  "scope": { "id": "/subscriptions/sub-1", "displayName": "Pay-As-You-Go", "type": "subscription" },
                  "roleDefinition": { "displayName": "Contributor" }
                }
              }
            },
            {
              "name": "0a1b2c3d-0000-4000-8000-000000000001",
              "properties": {
                "scope": "/subscriptions/sub-1",
                "roleDefinitionId": "/subscriptions/sub-1/providers/Microsoft.Authorization/roleDefinitions/acdd72a7-3385-48ef-bd42-f606fba81ae7",
                "requestType": "SelfActivate",
                "status": "Provisioned",
                "expandedProperties": { "roleDefinition": { "displayName": "Reader" } }
              }
            },
            {
              "name": "0a1b2c3d-0000-4000-8000-000000000002",
              "properties": {
                "scope": "/subscriptions/sub-1",
                "roleDefinitionId": "/subscriptions/sub-1/providers/Microsoft.Authorization/roleDefinitions/acdd72a7-3385-48ef-bd42-f606fba81ae7",
                "requestType": "AdminAssign",
                "status": "PendingApproval",
                "expandedProperties": { "roleDefinition": { "displayName": "Reader" } }
              }
            }
          ]
        }
        """;

    [Fact]
    public async Task GetPendingApprovalsAsync_ListsMyRequestsAtTenantRootAndKeepsPendingActivations()
    {
        var handler = new FakeHttpMessageHandler(FakeHttpMessageHandler.JsonResponse(MyRequests));
        var service = Build(handler);

        var result = await service.GetPendingApprovalsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            "https://management.azure.com/providers/Microsoft.Authorization/roleAssignmentScheduleRequests?api-version=2020-10-01&$filter=asRequestor()",
            handler.Requests[0].RequestUri!.ToString());

        var pending = Assert.Single(result);
        Assert.Equal(PimResourceKind.AzureResourceRole, pending.Kind);
        Assert.Equal("Contributor", pending.DisplayName);
        Assert.Equal(ContributorId, pending.ResourceId);
        Assert.Equal("/subscriptions/sub-1", pending.ScopeId);
        Assert.Equal("Subscription: Pay-As-You-Go", pending.ScopeLabel);
        Assert.Equal("fea7a502-9a96-4806-a26f-eee560e52045", pending.RequestId);
        Assert.Equal(TimeSpan.FromHours(4), pending.RequestedDuration);
    }

    [Fact]
    public async Task GetRequestStatusAsync_FindsTheRequestByName()
    {
        var handler = new FakeHttpMessageHandler(FakeHttpMessageHandler.JsonResponse(MyRequests));
        var service = Build(handler);

        var status = await service.GetRequestStatusAsync("0A1B2C3D-0000-4000-8000-000000000001", TestContext.Current.CancellationToken);

        Assert.Equal(ActivationStatus.Provisioned, status);
    }

    [Fact]
    public async Task GetRequestStatusAsync_NotListed_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(FakeHttpMessageHandler.JsonResponse(MyRequests));
        var service = Build(handler);

        Assert.Null(await service.GetRequestStatusAsync("99999999-0000-4000-8000-000000000000", TestContext.Current.CancellationToken));
    }

    private static PimAzureResourceService Build(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://management.azure.com/") },
        UserOid,
        NullLogger<PimAzureResourceService>.Instance);
}
