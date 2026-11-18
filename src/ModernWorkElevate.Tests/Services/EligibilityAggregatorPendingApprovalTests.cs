namespace ModernWorkElevate.Tests.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModernWorkElevate.Core.Arm;
using ModernWorkElevate.Core.Auth;
using ModernWorkElevate.Core.Configuration;
using ModernWorkElevate.Core.Models;
using ModernWorkElevate.Core.Services;
using Moq;

public sealed class EligibilityAggregatorPendingApprovalTests
{
    private static readonly IOptions<ModernWorkElevateOptions> NoOptions = Options.Create(new ModernWorkElevateOptions());

    [Fact]
    public async Task GetAggregatedPendingApprovalsAsync_MergesAllThreeSurfaces()
    {
        var account = MakeAccount();
        var (aggregator, role, group, azure) = Build(account);
        role.Setup(s => s.GetPendingApprovalsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Pending(PimResourceKind.DirectoryRole, "r-1")]);
        group.Setup(s => s.GetPendingApprovalsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Pending(PimResourceKind.GroupMembership, "g-1")]);
        azure.Setup(s => s.GetPendingApprovalsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Pending(PimResourceKind.AzureResourceRole, "a-1")]);

        var result = await aggregator.GetAggregatedPendingApprovalsAsync([account], TestContext.Current.CancellationToken);

        var fetched = result[account];
        Assert.Null(fetched.LoadError);
        Assert.Empty(fetched.UnreadKinds);
        Assert.Equal(["r-1", "g-1", "a-1"], fetched.Items.Select(item => item.RequestId));
    }

    [Fact]
    public async Task GetAggregatedPendingApprovalsAsync_AzureFails_KeepsGraphRowsAndSaysAzureIsUnread()
    {
        // The caller must not read "no Azure rows" as "every Azure request was decided".
        var account = MakeAccount();
        var (aggregator, role, group, azure) = Build(account);
        role.Setup(s => s.GetPendingApprovalsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Pending(PimResourceKind.DirectoryRole, "r-1")]);
        group.Setup(s => s.GetPendingApprovalsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        azure.Setup(s => s.GetPendingApprovalsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new ArmRequestException(403, "AuthorizationFailed", "denied"));

        var fetched = (await aggregator.GetAggregatedPendingApprovalsAsync([account], TestContext.Current.CancellationToken))[account];

        Assert.Null(fetched.LoadError);
        Assert.False(fetched.WasRead(PimResourceKind.AzureResourceRole));
        Assert.True(fetched.WasRead(PimResourceKind.DirectoryRole));
        Assert.Equal("r-1", Assert.Single(fetched.Items).RequestId);
    }

    [Fact]
    public async Task GetAggregatedPendingApprovalsAsync_DirectoryRolesRefused_OtherSurfacesStillReport()
    {
        // A tenant that refuses the directory-role request list must not freeze the
        // group and Azure requests of the same account.
        var account = MakeAccount();
        var (aggregator, role, group, azure) = Build(account);
        role.Setup(s => s.GetPendingApprovalsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("403"));
        group.Setup(s => s.GetPendingApprovalsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Pending(PimResourceKind.GroupMembership, "g-1")]);
        azure.Setup(s => s.GetPendingApprovalsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var fetched = (await aggregator.GetAggregatedPendingApprovalsAsync([account], TestContext.Current.CancellationToken))[account];

        Assert.Null(fetched.LoadError);
        Assert.False(fetched.WasRead(PimResourceKind.DirectoryRole));
        Assert.True(fetched.WasRead(PimResourceKind.GroupMembership));
        Assert.True(fetched.WasRead(PimResourceKind.AzureResourceRole));
        Assert.Equal("g-1", Assert.Single(fetched.Items).RequestId);
    }

    [Fact]
    public async Task GetAggregatedPendingApprovalsAsync_EverySurfaceFails_ReportsAnErrorInsteadOfAnEmptyList()
    {
        var account = MakeAccount();
        var (aggregator, role, group, azure) = Build(account);
        role.Setup(s => s.GetPendingApprovalsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("offline"));
        group.Setup(s => s.GetPendingApprovalsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("offline"));
        azure.Setup(s => s.GetPendingApprovalsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("offline"));

        var fetched = (await aggregator.GetAggregatedPendingApprovalsAsync([account], TestContext.Current.CancellationToken))[account];

        Assert.NotNull(fetched.LoadError);
        Assert.Empty(fetched.Items);
        Assert.False(fetched.WasRead(PimResourceKind.GroupOwnership));
    }

    [Theory]
    [InlineData(PimResourceKind.DirectoryRole)]
    [InlineData(PimResourceKind.GroupOwnership)]
    [InlineData(PimResourceKind.AzureResourceRole)]
    public async Task GetRequestStatusAsync_RoutesByKind(PimResourceKind kind)
    {
        var account = MakeAccount();
        var (aggregator, role, group, azure) = Build(account);
        role.Setup(s => s.GetRequestStatusAsync("id-1", It.IsAny<CancellationToken>())).ReturnsAsync(ActivationStatus.Provisioned);
        group.Setup(s => s.GetRequestStatusAsync("id-1", It.IsAny<CancellationToken>())).ReturnsAsync(ActivationStatus.Denied);
        azure.Setup(s => s.GetRequestStatusAsync("id-1", It.IsAny<CancellationToken>())).ReturnsAsync(ActivationStatus.TimedOut);

        var status = await aggregator.GetRequestStatusAsync(account, kind, "id-1", TestContext.Current.CancellationToken);

        var expected = kind switch
        {
            PimResourceKind.DirectoryRole => ActivationStatus.Provisioned,
            PimResourceKind.AzureResourceRole => ActivationStatus.TimedOut,
            _ => ActivationStatus.Denied,
        };
        Assert.Equal(expected, status);
    }

    [Fact]
    public async Task GetRequestStatusAsync_ReadFails_ReturnsNull()
    {
        var account = MakeAccount();
        var (aggregator, role, _, _) = Build(account);
        role.Setup(s => s.GetRequestStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("offline"));

        Assert.Null(await aggregator.GetRequestStatusAsync(account, PimResourceKind.DirectoryRole, "id-1", TestContext.Current.CancellationToken));
    }

    private static (EligibilityAggregator Aggregator, Mock<IPimRoleService> Role, Mock<IPimGroupService> Group, Mock<IPimAzureResourceService> Azure)
        Build(SignedInAccount account)
    {
        var role = new Mock<IPimRoleService>();
        var group = new Mock<IPimGroupService>();
        var azure = new Mock<IPimAzureResourceService>();
        var scoped = new Mock<IAccountScopedServices>();
        scoped.Setup(s => s.GetServicesFor(account))
            .Returns(new AccountScopedServiceBundle(role.Object, group.Object, azure.Object, Mock.Of<IPolicyService>()));
        return (new EligibilityAggregator(scoped.Object, NoOptions, NullLogger<EligibilityAggregator>.Instance), role, group, azure);
    }

    private static PendingApprovalRequest Pending(PimResourceKind kind, string requestId)
        => new(kind, "Role", "resource-1", "/", requestId, null, null);

    private static SignedInAccount MakeAccount() => new(
        ObjectId: "oid-a",
        TenantId: "tenant-a",
        Username: "oid-a@example.com",
        DisplayName: "User A",
        AddedAt: DateTimeOffset.UtcNow);
}
