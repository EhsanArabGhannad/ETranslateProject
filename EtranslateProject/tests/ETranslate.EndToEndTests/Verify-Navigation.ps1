#Requires -Version 7.0
param([string]$WebUrl='http://localhost:5067', [string]$IdentityUrl='http://localhost:5031',
    [string]$WorkflowUrl='http://localhost:5285', [string]$DocumentsUrl='http://localhost:5241',
    [string]$BillingUrl='http://localhost:5249', [string]$TestArtifactsPath='')
$ErrorActionPreference='Stop'
foreach ($url in @($WebUrl,$IdentityUrl,$WorkflowUrl,$DocumentsUrl,$BillingUrl)) {
    $uri=[Uri]$url
    if (-not $uri.IsAbsoluteUri -or -not $uri.IsLoopback -or $uri.Scheme -ne 'http') { throw 'Navigation fixtures require local HTTP URLs only.' }
}
. "$PSScriptRoot/Verify-TenantTeam.ps1" -WebUrl $WebUrl -IdentityUrl $IdentityUrl -WorkflowUrl $WorkflowUrl -DocumentsUrl $DocumentsUrl -BillingUrl $BillingUrl
$navigationBaselineChecks=$checks
$handler=[Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect=$false
$client=[Net.Http.HttpClient]::new($handler); $client.Timeout=[TimeSpan]::FromSeconds(45)
function Login-Navigation($Address,$Secret) {
    $page=Web GET '/Account/Login' 200
    Web POST '/Account/Login' 302 (Fields @{email=$Address;password=$Secret;__RequestVerificationToken=(Csrf $page.Body)}) | Out-Null
}
$navigationEnv=@('ETRANSLATE_NAV_BROWSER_TESTS','ETRANSLATE_NAV_WEB_URL','ETRANSLATE_NAV_TENANT','ETRANSLATE_NAV_OTHER_TENANT','ETRANSLATE_NAV_EMAIL','ETRANSLATE_NAV_PASSWORD','ETRANSLATE_NAV_QA_PATH','PLAYWRIGHT_BROWSERS_PATH')
$navigationOldEnv=@{}; foreach ($name in $navigationEnv) { $navigationOldEnv[$name]=[Environment]::GetEnvironmentVariable($name) }
try {
    Web GET "/Settings?tenantId=$tenantId" 302 | Out-Null
    Web GET "/Settings/Templates?tenantId=$tenantId" 302 | Out-Null
    Login-Navigation $email $password
    $workspace=Web GET "/Workspace?tenantId=$tenantId" 200
    Assert-Check ($workspace.Body -notmatch 'id="template-name"|class="logo-form"') 'Workspace contains jobs, not template forms'
    $settings=Web GET "/Settings?tenantId=$tenantId" 200
    Assert-Check ($settings.Body -match "/Settings/Templates\?tenantId=$tenantId") 'Settings opens tenant-scoped template management'
    $page=Web GET "/Settings/Templates?tenantId=$tenantId" 200
    Assert-Check ($page.Body -match 'id="template-name"' -and $page.Body -match 'id="template-edit-form"' -and $page.Body -match 'id="template-logo"') 'Unified template form remains on its own settings page'
    Assert-Check ($page.Body -notmatch 'access_token|Bearer |localStorage') 'Settings never exposes server tokens'
    Web GET "/Settings/Templates?tenantId=$([Guid]::NewGuid())" 404 | Out-Null
    Web GET '/Settings/Templates?tenantId=invalid' 400 | Out-Null
    Web GET '/Settings?tenantId=00000000-0000-0000-0000-000000000000' 400 | Out-Null
    Web POST '/Workspace/CreateTemplate' 400 (Fields @{tenantId=$tenantId;name='No CSRF'}) | Out-Null
    $result=Web POST '/Workspace/CreateTemplate' 302 (Fields @{tenantId=$tenantId;name='Navigation template';header='Header';__RequestVerificationToken=(Csrf $page.Body)})
    Assert-Check ($result.Location -eq "/Settings/Templates?tenantId=$tenantId") 'Existing create route returns to the new page with the same scope'
    $page=Web GET $result.Location 200
    $invalid=Web POST '/Workspace/AddLogo' 302 (Fields @{tenantId=$tenantId;templateId=$templateId;__RequestVerificationToken=(Csrf $page.Body)})
    Assert-Check ($invalid.Location -eq "/Settings/Templates?tenantId=$tenantId") 'Logo validation also returns to scoped settings'
    $solo=Api POST "$IdentityUrl/api/v1/tenants" @{name='Navigation independent';slug="nav-$runId";type='IndependentTranslator'} 201
    Await-SubscriptionProvisioning $solo.id
    $empty=Web GET "/Settings/Templates?tenantId=$($solo.id)" 200
    Assert-Check ($empty.Body -notmatch 'class="logo-form"' -and $empty.Body -notmatch 'Navigation template') 'Switching scope does not leak office templates'
    # Team baseline provides active reader/translator accounts for current role checks.
    Web POST '/Account/Logout' 302 (Fields @{__RequestVerificationToken=(Csrf $empty.Body)}) | Out-Null
    Login-Navigation $observer.Email $observer.Password
    $reader=Web GET "/Settings/Templates?tenantId=$tenantId" 200
    Assert-Check ($reader.Body -match '<fieldset[^>]*disabled="disabled"' -and $reader.Body -notmatch 'href="/Team') 'Reviewer reads templates without editing controls or manager menu'
    Web POST '/Workspace/CreateTemplate' 403 (Fields @{tenantId=$tenantId;name='Forbidden reader';__RequestVerificationToken=(Csrf $reader.Body)}) | Out-Null
    Web GET "/Settings/Templates?tenantId=$($solo.id)" 404 | Out-Null
    $env:ETRANSLATE_NAV_BROWSER_TESTS='1'; $env:ETRANSLATE_NAV_WEB_URL=$WebUrl
    $env:ETRANSLATE_NAV_TENANT=$tenantId; $env:ETRANSLATE_NAV_OTHER_TENANT=$solo.id
    $env:ETRANSLATE_NAV_EMAIL=$email; $env:ETRANSLATE_NAV_PASSWORD=$password
    $env:ETRANSLATE_NAV_QA_PATH=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../tmp/navigation-qa'))
    $env:PLAYWRIGHT_BROWSERS_PATH=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../.local/pdf-browsers'))
    $arguments=@('test',(Join-Path $PSScriptRoot '../ETranslate.DomainTests/ETranslate.DomainTests.csproj'),'--no-build','--no-restore','--filter','FullyQualifiedName~NavigationBrowserTests','--logger','console;verbosity=normal')
    if ($TestArtifactsPath) { $arguments+=@('--artifacts-path',[IO.Path]::GetFullPath($TestArtifactsPath)) }
    & dotnet @arguments
    Assert-Check ($LASTEXITCODE -eq 0) 'Real browser navigation/scoping and desktop/mobile overflow checks pass'
    Write-Output "PASS: $($checks-$navigationBaselineChecks) navigation checks plus $navigationBaselineChecks baseline/team checks and one real-browser test. Retained fixture: $tenantId"
} finally {
    foreach ($name in $navigationEnv) { [Environment]::SetEnvironmentVariable($name,$navigationOldEnv[$name]) }
    $client.Dispose(); $handler.Dispose()
}
