namespace ModernWorkElevate.AppAvalonia.ViewModels;

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ModernWorkElevate.Core.Auth;
using ModernWorkElevate.Core.Models;

/// <summary>
/// A row in the "Waiting for approval" list: an activation the user requested that an
/// approver has not decided yet. It carries the same user / tenant / type / scope lines
/// as an active row, so the role reads the same before and after the approval.
/// </summary>
public sealed partial class PendingApprovalItemViewModel : ObservableObject
{
    /// <summary>
    /// Tenant display name, pushed in by the shell once resolved; <c>null</c> until then.
    /// </summary>
    [ObservableProperty]
    private string? _tenantName;

    /// <summary>The user's short name for this enrollment, or <c>null</c> when none is set.</summary>
    [ObservableProperty]
    private string? _accountAlias;

    /// <summary>
    /// Subscription name for a resource-group scope, resolved by the shell like on the
    /// active rows; <c>null</c> falls back to the subscription id.
    /// </summary>
    [ObservableProperty]
    private string? _scopeSubscriptionName;

    public PendingApprovalItemViewModel(PendingApprovalRequest request, SignedInAccount account, DateTimeOffset trackedSince)
    {
        Request = request;
        Account = account;
        TrackedSince = trackedSince;
    }

    /// <summary>The request as the service reported it (or as this app filed it).</summary>
    public PendingApprovalRequest Request { get; }

    /// <summary>The enrollment the request belongs to.</summary>
    public SignedInAccount Account { get; }

    /// <summary>
    /// When this app first saw the request. A request filed moments ago may not be
    /// readable yet; the shell gives it a grace period before calling it closed.
    /// </summary>
    public DateTimeOffset TrackedSince { get; }

    /// <summary>Role or group name.</summary>
    public string DisplayName => Request.DisplayName;

    /// <inheritdoc cref="ActiveAssignmentItemViewModel.UserLine"/>
    public string UserLine => $"User: {AccountAlias ?? Account.Username}";

    /// <inheritdoc cref="ActiveAssignmentItemViewModel.TenantLine"/>
    public string TenantLine => $"Tenant: {TenantLabelFormatter.Format(TenantName, Account.TenantId)}";

    /// <inheritdoc cref="ActiveAssignmentItemViewModel.TypeLine"/>
    public string TypeLine => $"Type: {ResourceKindLabels.For(Request.Kind)}";

    /// <inheritdoc cref="ActiveAssignmentItemViewModel.ShowType"/>
    public bool ShowType => Request.Kind != PimResourceKind.AzureResourceRole;

    /// <inheritdoc cref="ActiveAssignmentItemViewModel.ScopeLine"/>
    public string ScopeLine => Request.Kind == PimResourceKind.AzureResourceRole
        ? $"Scope: {ScopeNameFormatter.Describe(Request.ScopeId, Request.ScopeLabel, ScopeSubscriptionName)}"
        : Request.ScopeLabel ?? string.Empty;

    /// <inheritdoc cref="ActiveAssignmentItemViewModel.HasScope"/>
    public bool HasScope => !string.IsNullOrWhiteSpace(Request.ScopeLabel);

    /// <summary>Tenant GUID behind the tenant line.</summary>
    public string TenantTooltip => Account.TenantId;

    /// <summary>Full scope path behind the scope line.</summary>
    public string ScopeTooltip => Request.ScopeId;

    /// <summary>
    /// "Requested 14:32 · 2h": when, in local time, and for how long — what someone
    /// chasing an approver is asked first.
    /// </summary>
    public string RequestedLine
    {
        get
        {
            var at = (Request.RequestedAt ?? TrackedSince).ToLocalTime();
            var when = at.Date == DateTimeOffset.Now.Date
                ? at.ToString("t", CultureInfo.CurrentCulture)
                : at.ToString("g", CultureInfo.CurrentCulture);
            return Request.RequestedDuration is { } duration && duration > TimeSpan.Zero
                ? $"Requested {when} · {FormatDuration(duration)}"
                : $"Requested {when}";
        }
    }

    /// <summary>What a screen reader announces for the row.</summary>
    public string AccessibleName => $"{DisplayName}, waiting for approval. {RequestedLine}.";

    private static string FormatDuration(TimeSpan duration)
        => duration.TotalHours >= 1
            ? duration.Minutes == 0 ? $"{(int)duration.TotalHours}h" : $"{(int)duration.TotalHours}h {duration.Minutes}m"
            : $"{duration.Minutes}m";

    partial void OnTenantNameChanged(string? value) => OnPropertyChanged(nameof(TenantLine));

    partial void OnAccountAliasChanged(string? value) => OnPropertyChanged(nameof(UserLine));

    partial void OnScopeSubscriptionNameChanged(string? value) => OnPropertyChanged(nameof(ScopeLine));
}
