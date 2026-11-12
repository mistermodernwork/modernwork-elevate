namespace ModernWorkElevate.Tests.Services;

using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using ModernWorkElevate.Core.Graph;
using ModernWorkElevate.Core.Models;
using ModernWorkElevate.Core.Services;
using ModernWorkElevate.Tests.TestSupport;

public sealed class PimRoleServiceTests
{
    private const string QueryPatternError = """
        { "error": { "code": "BadRequest", "message": "The query pattern is not supported by the service; if you are using '$expand' or '$select', some properties might not be retrievable." } }
        """;

    [Fact]
    public async Task GetEligibleRolesAsync_MapsDirectoryRoleEligibilities()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("directory-eligibilities.json")));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler), NullLogger<PimRoleService>.Instance);

        var result = await service.GetEligibleRolesAsync();

        Assert.Equal(2, result.Count);

        var globalAdmin = result[0];
        Assert.Equal(PimResourceKind.DirectoryRole, globalAdmin.Kind);
        Assert.Equal("Global Administrator", globalAdmin.DisplayName);
        Assert.Equal("role-def-ga", globalAdmin.ResourceId);
        Assert.Equal("/", globalAdmin.ScopeId);
        Assert.False(globalAdmin.IsRoleAssignableGroup);

        // directoryScopeId must be passed through verbatim — not normalized to "/".
        Assert.Equal("/administrativeUnits/au-1", result[1].ScopeId);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotImplemented)]
    public async Task GetEligibleRolesAsync_ScopeExpansionRefused_RetriesWithRoleNameOnly(HttpStatusCode status)
    {
        // Field case 2026-10-26: "The query pattern is not supported by the service" for
        // $expand=roleDefinition,directoryScope, and not necessarily as 400/403.
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(QueryPatternError, status),
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("directory-eligibilities.json")));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler), NullLogger<PimRoleService>.Instance);

        var result = await service.GetEligibleRolesAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("Global Administrator", result[0].DisplayName);
        Assert.Contains("directoryScope", Uri.UnescapeDataString(handler.Requests[0].RequestUri!.Query), StringComparison.Ordinal);
        Assert.DoesNotContain("directoryScope", Uri.UnescapeDataString(handler.Requests[1].RequestUri!.Query), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetEligibleRolesAsync_EveryExpansionRefused_StillReturnsTheList()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(QueryPatternError, HttpStatusCode.BadRequest),
            FakeHttpMessageHandler.JsonResponse(QueryPatternError, HttpStatusCode.BadRequest),
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("directory-eligibilities.json")));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler), NullLogger<PimRoleService>.Instance);

        var result = await service.GetEligibleRolesAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal(3, handler.RequestCount);
        Assert.DoesNotContain("expand", Uri.UnescapeDataString(handler.Requests[2].RequestUri!.Query), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetEligibleRolesAsync_Unauthorized_IsNotTreatedAsAnExpansionProblem()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse("""{ "error": { "code": "InvalidAuthenticationToken", "message": "Access token is empty." } }""", HttpStatusCode.Unauthorized));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler), NullLogger<PimRoleService>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() => service.GetEligibleRolesAsync());
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetActiveRolesAsync_MapsActivatedAssignments()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("directory-active-assignments.json")));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler), NullLogger<PimRoleService>.Instance);

        var result = await service.GetActiveRolesAsync();

        var assignment = Assert.Single(result);
        Assert.Equal(PimResourceKind.DirectoryRole, assignment.Kind);
        Assert.Equal("Global Administrator", assignment.DisplayName);
        Assert.Equal("assign-ga", assignment.AssignmentScheduleId);
        Assert.NotNull(assignment.EndDateTime);
    }

    [Fact]
    public async Task ActivateAsync_BuildsSelfActivateRequestWithVerbatimScopeAndTicket()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(
                FixtureLoader.Load("activation-provisioned.json"), HttpStatusCode.Created));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler), NullLogger<PimRoleService>.Instance);

        var eligibility = new PimEligibility(PimResourceKind.DirectoryRole, "User Administrator", "role-def-ua", "/administrativeUnits/au-1", "user-oid-1", null, false);
        var request = new ActivationRequest(eligibility, TimeSpan.FromHours(2), "Fixing a locked account", new TicketInfo("INC-555", "ServiceNow"));

        var result = await service.ActivateAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(ActivationStatus.Provisioned, result.Status);

        var body = handler.RequestBodies[0];
        Assert.NotNull(body);
        Assert.Contains("selfActivate", body, StringComparison.Ordinal);
        Assert.Contains("/administrativeUnits/au-1", body, StringComparison.Ordinal);
        Assert.Contains("INC-555", body, StringComparison.Ordinal);
        Assert.Contains("Fixing a locked account", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActivateAsync_PendingApprovalResponse_IsParsedFromBody()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(
                FixtureLoader.Load("activation-pending-approval.json"), HttpStatusCode.Created));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler), NullLogger<PimRoleService>.Instance);

        var result = await service.ActivateAsync(SampleRequest());

        // A 201 can still carry a non-immediate status — parsed from the body.
        Assert.Equal(ActivationStatus.PendingApproval, result.Status);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ActivateAsync_PolicyViolation_ReturnsMappedValidationError()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(
                FixtureLoader.Load("error-justification-required.json"), HttpStatusCode.BadRequest));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler), NullLogger<PimRoleService>.Instance);

        var result = await service.ActivateAsync(SampleRequest());

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorSeverity.Validation, result.Error.Severity);
        Assert.Equal("justification", result.Error.FieldHint);
    }

    [Fact]
    public async Task ActivateAsync_StartTimeInPast_RetriesOnceThenSucceeds()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(
                FixtureLoader.Load("error-start-time-in-past.json"), HttpStatusCode.BadRequest),
            FakeHttpMessageHandler.JsonResponse(
                FixtureLoader.Load("activation-provisioned.json"), HttpStatusCode.Created));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler), NullLogger<PimRoleService>.Instance);

        var result = await service.ActivateAsync(SampleRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task ActivateAsync_WithIsValidationOnly_SendsFlagInPostBody()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(
                FixtureLoader.Load("activation-provisioned.json"), HttpStatusCode.Created));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler), NullLogger<PimRoleService>.Instance);

        var eligibility = new PimEligibility(
            PimResourceKind.DirectoryRole, "Global Administrator", "role-def-ga", "/", "user-oid-1", null, false);
        var request = new ActivationRequest(
            eligibility, TimeSpan.FromHours(1), "Dry run", null, IsValidationOnly: true);

        await service.ActivateAsync(request);

        var body = handler.RequestBodies[0];
        Assert.NotNull(body);
        Assert.Contains("\"isValidationOnly\":true", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActivateAsync_WithAuthContextClaim_AttachesClaimsRequestOption()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(
                FixtureLoader.Load("activation-provisioned.json"), HttpStatusCode.Created));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler), NullLogger<PimRoleService>.Instance);

        await service.ActivateAsync(SampleRequest() with { AuthContextClaim = "c1" });

        var option = Assert.Single(handler.Requests[0].Options
            .Select(kv => kv.Value)
            .OfType<AuthContextRequestOption>());
        Assert.Contains("\"acrs\"", option.ClaimsJson, StringComparison.Ordinal);
        Assert.Contains("\"c1\"", option.ClaimsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActivateAsync_WithoutAuthContextClaim_AttachesNoClaimsRequestOption()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(
                FixtureLoader.Load("activation-provisioned.json"), HttpStatusCode.Created));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler), NullLogger<PimRoleService>.Instance);

        await service.ActivateAsync(SampleRequest());

        Assert.Empty(handler.Requests[0].Options
            .Select(kv => kv.Value)
            .OfType<AuthContextRequestOption>());
    }

    [Fact]
    public async Task ActivateAsync_AcrsValidationFailed_ReturnsStepUpRequired()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(
                FixtureLoader.Load("error-acrs-validation-failed.json"), HttpStatusCode.BadRequest));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler), NullLogger<PimRoleService>.Instance);

        var result = await service.ActivateAsync(SampleRequest());

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorSeverity.StepUpRequired, result.Error.Severity);
    }

    private static ActivationRequest SampleRequest()
    {
        var eligibility = new PimEligibility(PimResourceKind.DirectoryRole, "Global Administrator", "role-def-ga", "/", "user-oid-1", null, false);
        return new ActivationRequest(eligibility, TimeSpan.FromHours(1), "Test justification", null);
    }
}
