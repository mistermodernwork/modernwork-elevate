namespace ModernWorkElevate.Core.ErrorHandling;

using System.Text.RegularExpressions;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Identity.Client;
using ModernWorkElevate.Core.Arm;
using ModernWorkElevate.Core.Auth;
using ModernWorkElevate.Core.Configuration;
using ModernWorkElevate.Core.Models;

/// <summary>
/// Translates Graph <see cref="ODataError"/> and Azure Resource Manager
/// <see cref="ArmRequestException"/> responses into <see cref="UserFacingError"/>
/// based on the <c>error.code</c> and HTTP status. The two surfaces share most
/// PIM error codes. Raw service messages never reach the UI.
/// </summary>
public static class PimErrorMapper
{
    private const string MfaMessage = "This activation requires MFA verification. Please re-authenticate.";
    private const string JustificationMessage = "A justification is required.";
    private const string TicketMessage = "A ticket reference is required.";
    private const string DurationMessage = "The requested duration exceeds the allowed maximum.";
    private const string RefreshMessage = "Eligibility no longer available. Please refresh the list.";
    private const string GenericActivationMessage = "Activation failed. See the log file for details.";
    private const string SharedConsentMessage = "An admin of this tenant must consent to ModernWork Elevate once.";
    private const string SelfHostedConsentMessage = "The App Registration lacks admin consent in this tenant. An admin must grant it in Entra.";
    private const string DeviceCodeSharedHostedMessage = "Device-code sign-in needs a self-hosted App Registration for this tenant.";
    private const string NotAssignedMessage = "Your account is not assigned to ModernWork Elevate in this tenant. An admin assigns it under Enterprise applications.";
    private const string SharedDisabledMessage = "ModernWork Elevate is switched off for this tenant, either by an admin of this tenant or by its publisher for all tenants. Ask your admin.";
    private const string SelfHostedDisabledMessage = "The App Registration is disabled in this tenant.";
    private const string OfflineMessage = "No connection to Microsoft Entra. Please check your network connection.";
    private const string SignInRequiredMessage = "Sign-in required for this account. Click Refresh (↻) to sign in again.";
    private const string TenantRestrictionsMessage = "Your network's tenant restrictions block sign-in to this tenant. An admin of your organization's own tenant must allow this tenant (and the app) under External Identities → Cross-tenant access settings → Tenant restrictions.";
    private const string GenericFetchMessage = "Couldn't load eligibilities for this tenant. See the log file for details.";
    private const string AzureUnreachableMessage = "Azure Resource Manager could not be reached. A proxy or firewall may block it — Settings → Diagnostics → Network check shows which endpoint. The app retries on its own.";
    private const string AzureProxyAuthMessage = "The proxy asks for authentication before it lets the app reach Azure Resource Manager. Your network team must allow the Azure Resource Manager endpoint for this device without proxy sign-in. The app retries on its own.";
    private const string AzureTokenTransientMessage = "The background sign-in for Azure did not get through this time (network or Windows broker not ready). The app retries on its own.";
    private const string AzureGenericMessage = "Azure Resource Manager could not be read this time. The app retries on its own; the log file names the cause if it persists.";

    /// <summary>Maps a Graph error to a user-facing error.</summary>
    public static UserFacingError Map(ODataError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return Map(error.Error?.Code ?? string.Empty, error.ResponseStatusCode, error.Error?.Message);
    }

    /// <summary>Maps an Azure Resource Manager error to a user-facing error.</summary>
    public static UserFacingError Map(ArmRequestException error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return Map(error.Code, error.StatusCode, error.Detail);
    }

    /// <summary>
    /// Maps any exception raised by a Graph call into a <see cref="UserFacingError"/>.
    /// Covers the non-<see cref="ODataError"/> failure paths the UI must still present
    /// gracefully: a cancelled/timed-out operation and a missing network connection.
    /// Raw exception detail never reaches the UI — only the log.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <param name="hosting">
    /// Whose App Registration the tenant uses. A shared-hosted tenant's admin cannot change
    /// the shared app, so advice to fix the registration becomes advice to consent or ask.
    /// </param>
    /// <param name="addingAccount">
    /// True for a sign-in from the add-account panel, where a cancelled prompt is a
    /// cancelled sign-in rather than a role that was not activated.
    /// </param>
    public static UserFacingError MapException(
        Exception exception,
        AppRegistrationHosting hosting = AppRegistrationHosting.SelfHosted,
        bool addingAccount = false)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            ODataError odataError => Map(odataError),
            ArmRequestException armError => Map(armError),

            // Must precede the MsalServiceException arm: a WAM prompt the user
            // dismissed can surface as either MsalClientException or
            // MsalServiceException, both deriving from MsalException.
            MsalException { ErrorCode: MsalError.AuthenticationCanceledError } => MapCancel(hosting, addingAccount),

            // Every MsalException, not just the service kind: MSAL's UiRequired throttle
            // repeats a service failure as a client exception carrying the same AADSTS text.
            MsalException msal => MapMsal(msal, hosting),

            OperationCanceledException =>
                Error(ErrorSeverity.Timeout, "The request timed out. Please try again."),

            _ when IsNetworkFailure(exception) =>
                Error(ErrorSeverity.Offline, OfflineMessage),

            _ =>
                Error(ErrorSeverity.Fatal, "An unexpected error occurred. See the log file for details."),
        };
    }

    /// <summary>
    /// Returns the message to show for <paramref name="error"/> on an account
    /// enrolled with <paramref name="authMethod"/>. A device-code enrollment
    /// cannot satisfy a Conditional Access authentication context — that token
    /// path has no way to carry the required claim — so the generic "please try
    /// again" would send the user into a loop that can never succeed. Name the
    /// cause and the way out instead.
    /// </summary>
    public static string Describe(UserFacingError error, AuthMethod authMethod)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error.Severity == ErrorSeverity.StepUpRequired && authMethod == AuthMethod.DeviceCode
            ? "This role requires additional verification, which device-code sign-in cannot provide. "
              + "Remove this account in Settings and add it again using the standard sign-in."
            : error.Message;
    }

    /// <summary>
    /// Returns true when the error indicates the request start time was in the past
    /// (typically client/Microsoft clock skew) — a candidate for a single retry.
    /// </summary>
    public static bool IsStartTimeInPast(ODataError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var code = error.Error?.Code;
        return string.Equals(code, "StartTimeInPast", StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, "InvalidStartDateTime", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the caption to show on a tenant group whose eligibility fetch
    /// failed. The one cause worth naming is a tenant without a PIM licence —
    /// otherwise the group shows "(0)" and looks exactly like a working tenant
    /// with nothing to activate. Everything else stays generic; the detail is
    /// in the log.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <param name="authMethod">How the account signed in, when known.</param>
    /// <param name="hosting">Whose App Registration the tenant uses.</param>
    public static string DescribeFetchFailure(
        Exception exception,
        AuthMethod? authMethod = null,
        AppRegistrationHosting hosting = AppRegistrationHosting.SelfHosted)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // Graph refuses PIM reads on an unlicensed tenant with
        // AadPremiumLicenseRequired ("The tenant needs to have Microsoft Entra
        // ID P2 or Microsoft Entra ID Governance license."). The message match
        // is a safety net in case the code differs between the role and group
        // surfaces.
        if (exception is ODataError { Error: { } error }
            && (string.Equals(error.Code, "AadPremiumLicenseRequired", StringComparison.OrdinalIgnoreCase)
                || (error.Message?.Contains("Governance license", StringComparison.OrdinalIgnoreCase) ?? false)))
        {
            return "PIM is not available in this tenant: it has no Microsoft Entra ID P2 or Governance license.";
        }

        // The eligibility reads of an unlicensed tenant answered "The tenant has
        // not onboarded to PIM." instead (field test 2026-10-17). That names the
        // state, not its cause, so the caption names the usual cause as such.
        if (exception is ODataError { Error.Message: { } onboarding }
            && onboarding.Contains("tenant has not onboarded to PIM", StringComparison.OrdinalIgnoreCase))
        {
            return "PIM is not set up in this tenant, usually because it has no Microsoft Entra ID P2 or Governance license.";
        }

        // MsalAuthService found no cached MSAL account for this enrollment. The
        // realistic cause is an App Registration change (the tenant's entry got
        // a different client id), which moves the enrollment to a different MSAL
        // client whose cache has never seen it. Only a fresh sign-in can repair
        // that — say so instead of "see the log".
        if (exception is MsalUiRequiredException { ErrorCode: MsalError.UserNullError })
        {
            return "Sign-in for this account is no longer valid (its App Registration may have changed). Remove the account in Settings and add it again.";
        }

        // Tenant restrictions: the proxy (or Windows itself) adds a header to the sign-in
        // request that allows only the organization's own tenant. An admin account in a
        // customer tenant is then refused by Entra, not by the app — and no refresh fixes it.
        if (IsTenantRestricted(exception))
        {
            return TenantRestrictionsMessage;
        }

        // AADSTS65001: the tenant has not consented to a permission the app now
        // requests — every scope change reopens this window. The refresh token
        // stays valid and consent is checked at token issuance, so the account
        // recovers by itself once an admin consents; say so instead of "see the log".
        if (IsConsentMissing(exception))
        {
            return hosting == AppRegistrationHosting.SharedHosted
                ? "An admin of this tenant must consent to ModernWork Elevate once; the account then recovers on its own."
                : "This tenant has not consented to the app's current permissions. An admin must grant admin consent for the App Registration; the account then recovers on its own.";
        }

        // AADSTS50105 ("Assignment required" without an assignment) and a switched-off
        // app both stay until an admin acts, so a refresh cannot fix them either.
        if (exception is MsalException && HasAadsts(exception.Message, "50105"))
        {
            return NotAssignedMessage;
        }

        if (exception is MsalException && IsAppDisabled(exception.Message))
        {
            return hosting == AppRegistrationHosting.SharedHosted ? SharedDisabledMessage : SelfHostedDisabledMessage;
        }

        // AADSTS53000/53001: a Conditional Access policy demands a compliant or
        // domain-joined device. A device-code token carries no device claim at
        // all, so that enrollment can never satisfy the policy no matter how
        // often the list is refreshed — name the sign-in method as the cause and
        // point at the broker. On a broker account the device really is the
        // problem, so say that instead of sending the user off to re-add an
        // account that would land in exactly the same place. MsalException, not
        // MsalUiRequiredException: MSAL's UiRequired throttle wraps the original
        // in a different type on every repeat of the same failure.
        if (exception is MsalException
            && (exception.Message.Contains("AADSTS53001", StringComparison.Ordinal)
                || exception.Message.Contains("AADSTS53000", StringComparison.Ordinal)))
        {
            return authMethod == AuthMethod.DeviceCode
                ? "A Conditional Access policy requires a managed device, which device-code sign-in cannot present. Remove this account in Settings and add it again using the standard sign-in."
                : "A Conditional Access policy requires a managed device (domain-joined or compliant) and this device does not meet it.";
        }

        // The background refresh renews tokens silently and never opens a sign-in window
        // (InteractiveAuthScope); when Entra wants the user again — session expired, MFA
        // due, a password change — say so and name the click that opens the window.
        if (exception is MsalUiRequiredException
            || exception is MsalException { ErrorCode: MsalError.AuthenticationCanceledError })
        {
            return SignInRequiredMessage;
        }

        // Cancelled while acquiring the ARM token, not while waiting for Azure.
        // Keeping the two apart is what the 2026-09-04 field case cost: a token
        // problem reported as "the request timed out" sends the admin hunting for
        // a network fault that isn't there.
        if (exception is ArmSignInPendingException)
        {
            return "Signing in for this tenant's Azure permission didn't finish. If it keeps happening, the tenant has most likely not consented to the app's Azure permission — an admin must grant it.";
        }

        // Azure Resource Manager refused the read outright — typically
        // AuthorizationFailed while the token lacks the Azure Service
        // Management permission, or a tenant without any Azure subscriptions.
        if (exception is ArmRequestException)
        {
            return "Azure Resource Manager rejected the request. See the log file for details.";
        }

        return GenericFetchMessage;
    }

    /// <summary>
    /// <see cref="DescribeFetchFailure"/> for the Azure resource reads, which ask for their
    /// token silently only (<see cref="ArmBearerTokenHandler"/>) and so meet one failure the
    /// Graph reads never show this way.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <param name="authMethod">How the account signed in, when known.</param>
    /// <param name="hosting">Whose App Registration the tenant uses.</param>
    public static string DescribeAzureFetchFailure(
        Exception exception,
        AuthMethod? authMethod = null,
        AppRegistrationHosting hosting = AppRegistrationHosting.SelfHosted)
    {
        // WAM refused the silent request and hides the AADSTS code ("Error Message: (pii)",
        // field test 2026-10-17). Graph got its token, so Azure wants more: usually a consent
        // that covers Graph but not Azure — given in a sign-in window that asked for the
        // explicit Graph scopes (self-hosted, or shared-hosted before it asked for .default);
        // otherwise Conditional Access on Azure management, such as Microsoft's "require MFA
        // for Azure management" template. WAM reports both alike.
        if (exception is MsalUiRequiredException { ErrorCode: MsalError.FailedToAcquireTokenSilentlyFromBroker })
        {
            return hosting == AppRegistrationHosting.SharedHosted
                ? "The background sign-in for Azure was refused. Usually this tenant's admin consent for ModernWork Elevate does not cover Azure yet; an admin grants it under Enterprise applications → ModernWork Elevate → Permissions → Grant admin consent. Otherwise a Conditional Access policy for Azure management requires a stronger sign-in."
                : "The background sign-in for Azure was refused. Usually the tenant has not consented to the App Registration's Azure permission (Azure Service Management); otherwise a Conditional Access policy for Azure management requires a stronger sign-in.";
        }

        // A proxy that wants its own sign-in answers 407; Graph may be allowed through while
        // the Azure Resource Manager endpoint is not, which is exactly the mixed picture of a
        // tenant with directory roles but no Azure roles.
        if (FindHttpRequestException(exception) is { StatusCode: System.Net.HttpStatusCode.ProxyAuthenticationRequired })
        {
            return AzureProxyAuthMessage;
        }

        if (IsNetworkFailure(exception))
        {
            return AzureUnreachableMessage;
        }

        var described = DescribeFetchFailure(exception, authMethod, hosting);
        if (!string.Equals(described, GenericFetchMessage, StringComparison.Ordinal))
        {
            return described;
        }

        // Nothing specific matched. A transient token failure right after logon or wake-up
        // (no network yet, broker still starting) heals by itself, so say that instead of
        // the generic "couldn't load eligibilities", which reads like a broken tenant.
        return exception is MsalException && IsTransientFailure(exception)
            ? AzureTokenTransientMessage
            : AzureGenericMessage;
    }

    /// <summary>
    /// True when <paramref name="exception"/> is the kind of failure that may well be gone on
    /// the next attempt: a timeout, no network, a proxy or gateway hiccup, a broker that is not
    /// ready yet, or a service error Entra itself marks as retryable. False for failures that
    /// stay until someone acts — missing consent, Conditional Access, a disabled app — which are
    /// the only ones worth parking a tenant's Azure surface for.
    /// </summary>
    /// <param name="exception">The failure.</param>
    public static bool IsTransientFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException || IsNetworkFailure(exception))
        {
            return true;
        }

        return exception switch
        {
            // Needs a person: consent, MFA, Conditional Access, a re-added account.
            MsalUiRequiredException => false,
            MsalServiceException { IsRetryable: true } => true,
            MsalServiceException service => service.StatusCode is 0 or >= 500,

            // Client-side MSAL failures without an AADSTS code: no network, a timeout, or the
            // Windows broker not answering yet — typical in the first minute after logon.
            MsalClientException client => !client.Message.Contains("AADSTS", StringComparison.Ordinal),
            _ => false,
        };
    }

    private static UserFacingError Map(string code, int statusCode, string? message) => code switch
    {
        "RoleAssignmentExists" or "RoleAssignmentInstanceAlreadyExists" or "RoleAssignmentAlreadyExists" =>
            Error(ErrorSeverity.AlreadyActive, "This role is already active."),

        "JustificationRuleViolated" or "JustificationRequired" =>
            Error(ErrorSeverity.Validation, JustificationMessage, "justification"),

        "TicketingRuleViolated" or "TicketInfoRequired" =>
            Error(ErrorSeverity.Validation, TicketMessage, "ticket"),

        "MfaRuleViolated" or "MfaRuleNotSatisfied" or "MfaRequired" =>
            Error(ErrorSeverity.StepUpRequired, MfaMessage),

        "MaximumDurationExceeded" or "ScheduleExpirationRuleViolated" =>
            Error(ErrorSeverity.Validation, DurationMessage, "duration"),

        "StartTimeInPast" or "InvalidStartDateTime" =>
            Error(ErrorSeverity.Validation, "The start time is in the past. Please check the system clock."),

        // ARM folds every policy failure into one code and names the failed
        // rule in the message: 'The following policy rules failed: ["MfaRule"]'.
        "RoleAssignmentRequestPolicyValidationFailed" => MapPolicyRuleFailure(message),

        // ARM: self-deactivation inside Microsoft's 5-minute minimum active duration.
        "ActiveDurationTooShort" =>
            Error(ErrorSeverity.Validation, "The role was activated less than 5 minutes ago and cannot be deactivated yet."),

        "InvalidRoleAssignmentRequestSchedule" =>
            Error(ErrorSeverity.Validation, "The requested activation schedule was rejected. Check the duration and the system clock.", "duration"),

        "InvalidScope" or "ScopeNotAllowed" =>
            Error(ErrorSeverity.Fatal, "Invalid scope for this activation."),

        "EligibilityNotFound" or "RoleAssignmentDoesNotExist" or "ResourceNotFound" =>
            Error(ErrorSeverity.RefreshList, RefreshMessage),

        "ConcurrentActivationInProgress" =>
            Error(ErrorSeverity.RefreshList, "Another activation request is already in progress."),

        "InsufficientPermissions" or "Authorization_RequestDenied" or "AuthorizationFailed" =>
            Error(ErrorSeverity.Fatal, "Missing permission. Please contact your administrator."),

        // The app asked for a scope this tenant never consented to. Only a
        // tenant admin can fix it, and only on the App Registration — say
        // which side the problem is on instead of "contact your admin".
        "PermissionScopeNotGranted" =>
            Error(ErrorSeverity.Fatal, "This tenant has not granted the App Registration all required permissions. An admin must re-grant admin consent for it."),

        "RoleAssignmentApprovalRequired" =>
            Error(ErrorSeverity.Info, "This activation requires approval. The request has been submitted."),

        // RoleAssignmentRequestAcrsValidationFailed on the directory-role and
        // ARM surfaces; substring match also covers whatever the group surface
        // calls it — the exact code lands in the log either way.
        _ when code.Contains("AcrsValidationFailed", StringComparison.OrdinalIgnoreCase) =>
            Error(ErrorSeverity.StepUpRequired, "This activation requires additional identity verification, which could not be completed. Please try again."),

        _ when statusCode == 429 =>
            Error(ErrorSeverity.Throttled, "Too many requests. Please wait a moment."),

        _ when statusCode is 500 or 503 =>
            Error(ErrorSeverity.Fatal, "The Microsoft service is currently unavailable. Please try again later."),

        _ =>
            Error(ErrorSeverity.Fatal, GenericActivationMessage),
    };

    /// <summary>
    /// ARM reports which end-user rule an activation violated only inside the
    /// message text, so the rule name decides the severity and field hint.
    /// </summary>
    private static UserFacingError MapPolicyRuleFailure(string? message)
    {
        var rules = message ?? string.Empty;
        if (rules.Contains("MfaRule", StringComparison.OrdinalIgnoreCase))
        {
            return Error(ErrorSeverity.StepUpRequired, MfaMessage);
        }

        if (rules.Contains("JustificationRule", StringComparison.OrdinalIgnoreCase))
        {
            return Error(ErrorSeverity.Validation, JustificationMessage, "justification");
        }

        if (rules.Contains("TicketingRule", StringComparison.OrdinalIgnoreCase))
        {
            return Error(ErrorSeverity.Validation, TicketMessage, "ticket");
        }

        if (rules.Contains("ExpirationRule", StringComparison.OrdinalIgnoreCase))
        {
            return Error(ErrorSeverity.Validation, DurationMessage, "duration");
        }

        if (rules.Contains("EligibilityRule", StringComparison.OrdinalIgnoreCase))
        {
            return Error(ErrorSeverity.RefreshList, RefreshMessage);
        }

        return Error(ErrorSeverity.Fatal, GenericActivationMessage);
    }

    /// <summary>
    /// Walks the exception chain looking for a connectivity failure (a failed HTTP
    /// request or a socket error), which the Graph SDK surfaces wrapped at varying depths.
    /// </summary>
    /// <summary>
    /// AADSTS5000211 (tenant restrictions v2: "A tenant restrictions policy added to this
    /// request by a device or network administrator does not allow access") and its v1
    /// predecessor AADSTS500021 ("Access to tenant is denied", Restrict-Access-To-Tenants).
    /// </summary>
    private static bool IsTenantRestricted(Exception exception)
        => exception is MsalException
            && (HasAadsts(exception.Message, "5000211") || HasAadsts(exception.Message, "500021"));

    private static HttpRequestException? FindHttpRequestException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException http)
            {
                return http;
            }
        }

        return null;
    }

    private static bool IsNetworkFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException or System.Net.Sockets.SocketException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when <paramref name="message"/> carries exactly one of the AADSTS
    /// <paramref name="codes"/> (an alternation such as <c>"65001|65004"</c>). The lookahead
    /// is the point: a plain substring test cannot tell AADSTS70001 from AADSTS700016 or
    /// AADSTS7000112.
    /// </summary>
    private static bool HasAadsts(string message, string codes)
        => Regex.IsMatch(message, $@"AADSTS(?:{codes})(?!\d)");

    /// <summary>
    /// Missing consent in any of the shapes Entra and MSAL report it: AADSTS65001 (no grant),
    /// AADSTS65004 (consent declined), AADSTS90094 (admin consent required), or MSAL's own
    /// classification. Matched on <see cref="MsalException"/>, because MSAL's UiRequired
    /// throttle wraps a repeated failure in a different type.
    /// </summary>
    private static bool IsConsentMissing(Exception exception)
        => exception is MsalUiRequiredException { Classification: UiRequiredExceptionClassification.ConsentRequired }
            || (exception is MsalException
                && HasAadsts(exception.Message, "65001|65004|90094"));

    /// <summary>
    /// AADSTS7000112 / AADSTS70001: the app is switched off — by its publisher for every tenant
    /// (<c>isDisabled</c>) or by the tenant's own admin ("Enabled for users to sign-in?" = No).
    /// The code does not say which.
    /// </summary>
    private static bool IsAppDisabled(string message)
        => HasAadsts(message, "7000112|70001");

    private static UserFacingError MapCancel(AppRegistrationHosting hosting, bool addingAccount)
    {
        if (!addingAccount)
        {
            return Error(ErrorSeverity.Info, "Verification was cancelled. The role was not activated.");
        }

        // Entra shows "admin approval required" inside the WAM window, and closing that window
        // most likely arrives here as a cancel — so on a shared-hosted tenant the consent hint
        // (and, in the panel, the link) goes along with it.
        return hosting == AppRegistrationHosting.SharedHosted
            ? new UserFacingError(
                ErrorSeverity.Info,
                "Sign-in was cancelled. If Entra asked for an admin's approval, an admin of this tenant must consent to ModernWork Elevate once.",
                null,
                ConsentMissing: true)
            : Error(ErrorSeverity.Info, "Sign-in was cancelled.");
    }

    /// <summary>
    /// Maps an MSAL sign-in/token error (e.g. from the device-code or broker flow)
    /// into a user-facing message. Keyed on the Entra <c>AADSTS</c> code where a
    /// specific, actionable hint helps the admin fix their own configuration.
    /// </summary>
    private static UserFacingError MapMsal(MsalException msal, AppRegistrationHosting hosting)
    {
        // Device code is off unless a machine policy allows it
        // (MsalAuthService.RequireDeviceCodeAllowed). Nothing to retry; the message says why.
        if (msal.ErrorCode == "device_code_disabled")
        {
            return Error(ErrorSeverity.Fatal, msal.Message);
        }

        // Raised by MsalAuthService before it can even build a PCA: no App
        // Registration is pinned to the tenant (typically an enrollment whose
        // entry was removed from Settings). The message names tenant and cloud.
        if (msal.ErrorCode == "app_registration_missing")
        {
            return Error(
                ErrorSeverity.Fatal,
                $"{msal.Message} Open Settings → Tenants and add an entry for that tenant.");
        }

        // Raised by MsalAuthService when Entra issued the token for a different
        // tenant than the entry asked for. Should not happen with .WithTenantId;
        // the enrollment was discarded rather than persisted under the wrong entry.
        if (msal.ErrorCode == "tenant_mismatch")
        {
            return Error(
                ErrorSeverity.Fatal,
                "The sign-in ended up in a different tenant than the selected entry. Check the entry's tenant id in Settings → Tenants and try again.");
        }

        // Raised by MsalAuthService before any token request: the shared-hosted
        // registration has public client flows off, so device code cannot work there.
        if (msal.ErrorCode == "device_code_shared_hosted")
        {
            return Error(ErrorSeverity.Fatal, DeviceCodeSharedHostedMessage);
        }

        if (IsTenantRestricted(msal))
        {
            return Error(ErrorSeverity.Fatal, TenantRestrictionsMessage);
        }

        if (IsConsentMissing(msal))
        {
            return new UserFacingError(
                ErrorSeverity.Fatal,
                hosting == AppRegistrationHosting.SharedHosted ? SharedConsentMessage : SelfHostedConsentMessage,
                null,
                ConsentMissing: true);
        }

        if (HasAadsts(msal.Message, "50105"))
        {
            return Error(ErrorSeverity.Fatal, NotAssignedMessage);
        }

        if (IsAppDisabled(msal.Message))
        {
            return Error(ErrorSeverity.Fatal, hosting == AppRegistrationHosting.SharedHosted ? SharedDisabledMessage : SelfHostedDisabledMessage);
        }

        // AADSTS700016: the client id is unknown in the directory it was sent to.
        // Either a cloud mismatch — a Global client id cannot exist in the 21Vianet
        // directory (and vice versa) — or a single-tenant client id paired with
        // the wrong tenant id, or the tenant simply never consented to the app.
        // For a shared-hosted tenant only the last one is the customer's to fix.
        if (HasAadsts(msal.Message, "700016"))
        {
            return hosting == AppRegistrationHosting.SharedHosted
                ? new UserFacingError(
                    ErrorSeverity.Fatal,
                    "Entra does not know ModernWork Elevate in this tenant yet. " + SharedConsentMessage,
                    null,
                    ConsentMissing: true)
                : Error(
                    ErrorSeverity.Fatal,
                    "This app registration is unknown in the selected tenant. Check the entry's tenant id, client id and cloud in Settings → Tenants, and that admin consent was granted in that tenant.");
        }

        // AADSTS7000218: the token endpoint demanded a client secret/assertion,
        // i.e. the app registration is treated as a confidential client. A desktop
        // app is a PUBLIC client and cannot ship a secret — the registration is
        // missing "Allow public client flows". This is the federated-IdP /
        // device-code escape-hatch failure mode (see federated-idp-sso-wrong-account).
        // The shared registration has the flag off on purpose, and the customer cannot
        // change it — only device code reaches AADSTS7000218 there, and that needs
        // self-hosted. Any other invalid_client on the shared app stays generic.
        var demandsSecret = HasAadsts(msal.Message, "7000218");
        if (hosting == AppRegistrationHosting.SharedHosted && demandsSecret)
        {
            return Error(ErrorSeverity.Fatal, DeviceCodeSharedHostedMessage);
        }

        if (hosting == AppRegistrationHosting.SelfHosted && (demandsSecret || msal.ErrorCode == MsalError.InvalidClient))
        {
            return Error(
                ErrorSeverity.Fatal,
                "This app registration is not configured for desktop sign-in. In Entra, open the app registration → Authentication → enable \"Allow public client flows\", then try again.");
        }

        // AADSTS70016 is the normal "user hasn't entered the code yet" poll
        // response; MSAL handles it internally and it should never surface here.
        // Any other error: keep it generic, the detail is in the log — unless the
        // network is the cause, which the user can fix without the log.
        return IsNetworkFailure(msal)
            ? Error(ErrorSeverity.Offline, OfflineMessage)
            : Error(ErrorSeverity.Fatal, "Sign-in failed. See the log file for details.");
    }

    private static UserFacingError Error(ErrorSeverity severity, string message, string? fieldHint = null) =>
        new(severity, message, fieldHint);
}
