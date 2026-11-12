#requires -Version 7

<#
.SYNOPSIS
    Creates and consents the Entra App Registration for ModernWork Elevate.

.DESCRIPTION
    Automates steps 1-4 of docs/app-registration-setup.md: creates the registration,
    adds the WAM broker redirect URI, keeps public client flows off (no device code, no ROPC), requests the
    delegated permissions the app needs, and grants admin consent in the tenant you
    sign in to.

    Run this once per tenant, as an admin who may create applications and grant
    consent. It prints the two command lines an unattended rollout needs — see
    docs/unattended-deployment.md.

    The Graph scopes are read from the app's own appsettings.json rather than
    duplicated here, so this script cannot drift from what the app requests.

.PARAMETER DisplayName
    Name of the App Registration in Entra.

.PARAMETER SignInAudience
    AzureADMyOrg for a registration used in exactly one tenant, AzureADMultipleOrgs
    for one that several tenants consent to. Never a value including personal
    Microsoft accounts.

.PARAMETER Cloud
    Global or China. Selects the Graph endpoint to sign in to and the authority the
    printed consent URL points at.

.PARAMETER Label
    Optional alias for the tenant, shown in the app's sign-in picker, e.g. the
    customer's name. Only used in the printed command line.

.PARAMETER TicketSystem
    Optional ticketing system for this tenant, e.g. "ServiceNow". Only used in the
    printed command line.

.EXAMPLE
    ./create-app-registration.ps1

.EXAMPLE
    ./create-app-registration.ps1 -SignInAudience AzureADMultipleOrgs -Label "Contoso"
#>
param(
    [string]$DisplayName = "ModernWork Elevate",
    [string]$SignInAudience = "AzureADMyOrg",
    [string]$Cloud = "Global",
    [string]$Label = "",
    [string]$TicketSystem = ""
)

$ErrorActionPreference = "Stop"

# First-party resources the app requests delegated permissions from. These are the
# same in every tenant and every cloud — the per-tenant service principals below are
# not, which is why the scope GUIDs are resolved rather than hardcoded.
$graphAppId = "00000003-0000-0000-c000-000000000000"
$armAppId = "797f4846-ba00-4fd7-ba43-dac1f8f63013"
$armScope = "user_impersonation"

if ($Cloud -notin @("Global", "China")) {
    throw "Cloud must be 'Global' or 'China', got '$Cloud'."
}

if ($SignInAudience -notin @("AzureADMyOrg", "AzureADMultipleOrgs")) {
    throw "SignInAudience must be 'AzureADMyOrg' or 'AzureADMultipleOrgs', got '$SignInAudience'."
}

Write-Host "Creating the App Registration '$DisplayName' ($Cloud)" -ForegroundColor Cyan

# 1. Read the delegated Graph scopes from the app's shipped configuration. That file
#    is the authority on the permission surface (see docs/app-registration-setup.md), so deriving the list
#    from it keeps this script correct when the app's scopes change.
$appSettingsPath = Join-Path $PSScriptRoot "..\src\ModernWorkElevate.App.Avalonia\appsettings.json"
if (-not (Test-Path $appSettingsPath)) {
    throw "Could not find $appSettingsPath — run this script from a checkout of the repository."
}

$graphScopes = (Get-Content $appSettingsPath -Raw | ConvertFrom-Json).ModernWorkElevate.Scopes
if (-not $graphScopes) {
    throw "No ModernWorkElevate:Scopes found in $appSettingsPath."
}

Write-Host "  Delegated Graph scopes: $($graphScopes -join ', ')" -ForegroundColor DarkGray

# 2. Sign in. Creating the registration and consenting to it are two different rights;
#    both are needed, and both are the admin's, not the app's.
if (-not (Get-Module -ListAvailable -Name Microsoft.Graph.Applications)) {
    throw "The Microsoft.Graph.Applications module is missing. Install it with: Install-Module Microsoft.Graph.Applications -Scope CurrentUser"
}

$connectArgs = @{ Scopes = @("Application.ReadWrite.All", "DelegatedPermissionGrant.ReadWrite.All") }
if ($Cloud -eq "China") {
    $connectArgs.Environment = "China"
}

Write-Host "Signing in to Microsoft Graph..." -ForegroundColor Yellow
Connect-MgGraph @connectArgs | Out-Null
$tenantId = (Get-MgContext).TenantId
Write-Host "  Tenant: $tenantId" -ForegroundColor DarkGray

# 3. Resolve the delegated permission GUIDs from the resources' service principals.
#    Azure Service Management has no service principal in every tenant — a tenant that
#    never used ARM has to have one created before it can be consented to.
$graphSp = Get-MgServicePrincipal -Filter "appId eq '$graphAppId'"
if (-not $graphSp) {
    throw "No Microsoft Graph service principal in tenant $tenantId — this should not happen."
}

$armSp = Get-MgServicePrincipal -Filter "appId eq '$armAppId'"
if (-not $armSp) {
    Write-Host "Creating the Azure Service Management service principal..." -ForegroundColor Yellow
    $armSp = New-MgServicePrincipal -AppId $armAppId
}

$graphScopeIds = @{}
foreach ($permission in $graphSp.Oauth2PermissionScopes) {
    $graphScopeIds[$permission.Value] = $permission.Id
}

$resourceAccess = @()
foreach ($scope in $graphScopes) {
    if (-not $graphScopeIds.ContainsKey($scope)) {
        throw "Microsoft Graph does not publish a delegated permission named '$scope' in this tenant."
    }

    $resourceAccess += @{ Id = $graphScopeIds[$scope]; Type = "Scope" }
}

$armScopeId = ($armSp.Oauth2PermissionScopes | Where-Object { $_.Value -eq $armScope }).Id
if (-not $armScopeId) {
    throw "Azure Service Management does not publish a delegated permission named '$armScope'."
}

# 4. Create the registration. The redirect URI cannot be set yet — it embeds the
#    client id, which does not exist until the application has been created.
$app = New-MgApplication `
    -DisplayName $DisplayName `
    -SignInAudience $SignInAudience `
    -IsFallbackPublicClient:$false `
    -RequiredResourceAccess @(
        @{ ResourceAppId = $graphAppId; ResourceAccess = $resourceAccess },
        @{ ResourceAppId = $armAppId; ResourceAccess = @(@{ Id = $armScopeId; Type = "Scope" }) }
    )

Write-Host "  Application (client) ID: $($app.AppId)" -ForegroundColor DarkGray

# 5. Now that the client id exists, add the WAM broker redirect URI.
#    "Allow public client flows" stays OFF (IsFallbackPublicClient false) — the WAM flow
#    identifies itself through this redirect URI and does not need it, and with the flag
#    off Entra itself refuses device-code and ROPC (username/password) requests for the app.
Update-MgApplication -ApplicationId $app.Id -PublicClient @{
    RedirectUris = @("ms-appx-web://microsoft.aad.brokerplugin/$($app.AppId)")
}

# 6. Give the application a service principal in this tenant and consent to it.
#    Directory replication makes the application briefly invisible to this call.
$appSp = $null
foreach ($attempt in 1..10) {
    try {
        $appSp = New-MgServicePrincipal -AppId $app.AppId
        break
    }
    catch {
        if ($attempt -eq 10) {
            throw
        }

        Write-Host "  Waiting for the application to replicate (attempt $attempt)..." -ForegroundColor DarkGray
        Start-Sleep -Seconds 3
    }
}

Write-Host "Granting admin consent in tenant $tenantId..." -ForegroundColor Yellow
New-MgOauth2PermissionGrant `
    -ClientId $appSp.Id `
    -ConsentType "AllPrincipals" `
    -ResourceId $graphSp.Id `
    -Scope ($graphScopes -join " ") | Out-Null

New-MgOauth2PermissionGrant `
    -ClientId $appSp.Id `
    -ConsentType "AllPrincipals" `
    -ResourceId $armSp.Id `
    -Scope $armScope | Out-Null

# 7. Print what a rollout needs. current\ is the executable the installer's own install hook
#    invokes and replaces in place on update — not the launcher stub one level up,
#    whose argument forwarding is unverified.
$exePath = '"%ProgramFiles%\ModernWork Elevate\ModernWorkElevate.exe"'
$configureArgs = "--tenant-id $tenantId --client-id $($app.AppId)"
if ($Cloud -ne "Global") {
    $configureArgs += " --cloud $Cloud"
}

if ($Label) {
    $configureArgs += " --label `"$Label`""
}

if ($TicketSystem) {
    $configureArgs += " --ticket-system `"$TicketSystem`""
}

$scriptArgs = "-TenantId $tenantId -ClientId $($app.AppId)"
if ($Cloud -ne "Global") {
    $scriptArgs += " -Cloud $Cloud"
}

if ($Label) {
    $scriptArgs += " -Label `"$Label`""
}

if ($TicketSystem) {
    $scriptArgs += " -TicketSystem `"$TicketSystem`""
}

Write-Host ""
Write-Host "Done. Pre-configure this tenant for every device with the machine policy" -ForegroundColor Green
Write-Host "(ADMX 'Pre-configured tenants and App Registrations', or the registry):" -ForegroundColor Green
Write-Host ""
$policyValue = "$($app.AppId)"
if ($Label -or $Cloud -ne "Global") { $policyValue += ";$Label" }
if ($Cloud -ne "Global") { $policyValue += ";$Cloud" }
Write-Host "    HKLM\SOFTWARE\Policies\ModernWorkElevate\Tenants  $tenantId = $policyValue"
Write-Host ""
Write-Host "Or per user, after the MSI is installed (see docs/unattended-deployment.md):" -ForegroundColor Cyan
Write-Host ""
Write-Host "    $exePath $configureArgs"
Write-Host ""

if ($SignInAudience -eq "AzureADMultipleOrgs") {
    $authority = if ($Cloud -eq "China") { "login.partner.microsoftonline.cn" } else { "login.microsoftonline.com" }
    $redirectUri = "ms-appx-web://microsoft.aad.brokerplugin/$($app.AppId)"
    Write-Host "Every further tenant needs its own admin consent. Send its admin to:" -ForegroundColor Cyan
    Write-Host "    https://$authority/{tenant-id}/adminconsent?client_id=$($app.AppId)&redirect_uri=$redirectUri"
    Write-Host ""
}
