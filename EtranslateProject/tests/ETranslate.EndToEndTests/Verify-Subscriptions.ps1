#Requires -Version 7.0
param(
    [string]$WebUrl = 'https://localhost:7049',
    [string]$IdentityUrl = 'http://localhost:5031',
    [string]$WorkflowUrl = 'http://localhost:5285',
    [string]$DocumentsUrl = 'http://localhost:5241',
    [string]$BillingUrl = 'http://localhost:5249',
    [switch]$EmitBrowserFixture,
    [string]$HtmlOutputDirectory,
    [switch]$TestExpiredFixture
)
$ErrorActionPreference = 'Stop'
if ($TestExpiredFixture) {
    foreach ($localUrl in @($WebUrl, $IdentityUrl, $WorkflowUrl, $DocumentsUrl, $BillingUrl)) {
        $localUri = [Uri]$localUrl
        if (-not $localUri.IsAbsoluteUri -or -not $localUri.IsLoopback -or $localUri.Scheme -notin @('http', 'https')) {
            throw 'Expired fixture setup is allowed only when every service URL is local HTTP/HTTPS.'
        }
    }
}
$emitSubscriptionFixture = $EmitBrowserFixture; $subscriptionBillingUrl = $BillingUrl
$subscriptionHtmlDirectory = $HtmlOutputDirectory
$testSubscriptionExpiredFixture = $TestExpiredFixture
. "$PSScriptRoot/Verify-TenantTeam.ps1" -WebUrl $WebUrl -IdentityUrl $IdentityUrl -WorkflowUrl $WorkflowUrl -DocumentsUrl $DocumentsUrl -BillingUrl $BillingUrl
$subscriptionPriorChecks = $checks
$handler = [Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect = $false
$client = [Net.Http.HttpClient]::new($handler); $client.Timeout = [TimeSpan]::FromSeconds(45)
$subscriptionApi = "$subscriptionBillingUrl/api/v1/tenants/$tenantId/subscription"
$legacySubscriptionApi = "$subscriptionBillingUrl/internal/v1/subscriptions/$tenantId"
$subscriptionPage = "/Subscription?tenantId=$tenantId"
function Read-Subscription($Headers = $bearer) {
    Api GET $subscriptionApi $null 200 $Headers
}
function Await-Trial([string]$Url) {
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        $response = Invoke-WebRequest -Uri $Url -Headers $bearer -SkipHttpErrorCheck -TimeoutSec 10
        if ($response.StatusCode -eq 200) { return ConvertFrom-Json $response.Content }
        Assert-Check ($response.StatusCode -eq 202) 'Awaiting a subscription must only report Pending, never fake trial/expiry'
        $pending = ConvertFrom-Json $response.Content
        Assert-Check ($pending.status -eq 'Pending' -and $null -eq $pending.trialEndsAtUtc -and $null -eq $pending.remainingTrialDays) 'Pending must not invent dates or remaining time'
        Start-Sleep -Milliseconds 250
    }
    throw 'Trial did not become available within the development fixture wait budget'
}
function Login-Web($Address, $Secret) {
    $loginPage = Web GET '/Account/Login' 200
    Web POST '/Account/Login' 302 (Fields @{ email = $Address; password = $Secret; __RequestVerificationToken = (Csrf $loginPage.Body) }) | Out-Null
}
function Logout-Web($Page) {
    Web POST '/Account/Logout' 302 (Fields @{ __RequestVerificationToken = (Csrf $Page.Body) }) | Out-Null
}
function Save-SubscriptionHtml($Page, [string]$Name) {
    if (-not $subscriptionHtmlDirectory) { return }
    $target = [IO.Path]::GetFullPath($subscriptionHtmlDirectory)
    $null = New-Item -ItemType Directory -Path $target -Force
    # Read-only visual fixture: embed the actual stylesheet, with no scripts, forms or live links.
    $css = (Invoke-WebRequest "$WebUrl/css/site.css").Content
    $html = [regex]::Replace($Page.Body, '<link[^>]+href="/css/site\.css[^>]+>', "<style>$css</style>")
    $html = [regex]::Replace($html, '<script\b[^>]*>[\s\S]*?</script>', '')
    $html = [regex]::Replace($html, '<form\b[^>]*>[\s\S]*?</form>', '')
    $html = [regex]::Replace($html, '<a\b[^>]*>([\s\S]*?)</a>', '<span>$1</span>')
    [IO.File]::WriteAllText((Join-Path $target $Name), $html, [Text.UTF8Encoding]::new($false))
}
try {
    Api GET $subscriptionApi $null 401 @{} | Out-Null
    Api GET $legacySubscriptionApi $null 401 @{} | Out-Null
    Api GET $subscriptionApi $null 401 @{ Authorization = 'Bearer invalid-test-token' } | Out-Null
    Api GET $subscriptionApi $null 401 @{ 'X-Tenant-Id' = $tenantId } | Out-Null
    Api GET $subscriptionApi $null 403 $guest.Headers | Out-Null
    Api GET $legacySubscriptionApi $null 403 $guest.Headers | Out-Null
    Api GET "$subscriptionBillingUrl/api/v1/tenants/$([Guid]::NewGuid())/subscription" $null 403 | Out-Null
    $trial = Await-Trial $subscriptionApi
    Assert-Check ($trial.tenantId -eq $tenantId -and $trial.status -eq 'Trialing' -and $trial.planCode -eq 'trial') 'Office trial is tenant-scoped and not a paid subscription'
    Assert-Check (([DateTimeOffset]$trial.trialEndsAtUtc - [DateTimeOffset]$trial.trialStartedAtUtc).TotalDays -eq 30) 'Trial must remain exactly 30 days, not a calendar month'
    Assert-Check ($trial.remainingTrialDays -ge 1 -and $trial.remainingTrialDays -le 30 -and $trial.observedAtUtc) 'Snapshot supplies bounded remaining days and authoritative observation time'
    foreach ($member in @($bearer, $admin.Headers, $translator.Headers, $observer.Headers)) {
        $current = Read-Subscription $member
        Assert-Check ($current.trialStartedAtUtc -eq $trial.trialStartedAtUtc -and $current.trialEndsAtUtc -eq $trial.trialEndsAtUtc) 'All active members see the same trial without restarting it'
        Api GET $legacySubscriptionApi $null 200 $member | Out-Null
    }
    Active $translatorId $false | Out-Null
    Api GET $subscriptionApi $null 403 $translator.Headers | Out-Null
    Api GET $legacySubscriptionApi $null 403 $translator.Headers | Out-Null
    Active $translatorId $true | Out-Null
    Role $translatorId 'Reviewer' | Out-Null
    Read-Subscription $translator.Headers | Out-Null
    Role $translatorId 'Translator' | Out-Null
    Api GET "$subscriptionBillingUrl/api/v1/tenants/$($other.id)/subscription" $null 403 $translator.Headers | Out-Null
    $solo = Await-Trial "$subscriptionBillingUrl/api/v1/tenants/$($other.id)/subscription"
    Assert-Check ($solo.tenantId -eq $other.id -and $solo.status -eq 'Trialing') 'Independent workspace has its own separate trial'
    $raw = Invoke-WebRequest $subscriptionApi -Headers $bearer
    Assert-Check ($raw.Headers['Cache-Control'] -match 'private' -and $raw.Headers['Cache-Control'] -match 'no-store') 'Subscription API responses must be private/no-store'
    Api POST $subscriptionApi @{ status = 'Active' } 405 | Out-Null
    Api PUT $subscriptionApi @{ trialEndsAtUtc = [DateTimeOffset]::UtcNow.AddYears(1) } 405 | Out-Null
    Web GET $subscriptionPage 302 | Out-Null
    Login-Web $email $password
    $workspace = Web GET "/Workspace?tenantId=$tenantId" 200
    Assert-Check ($workspace.Body -match '/Subscription\?tenantId=') 'Workspace must link to the selected tenant subscription'
    $page = Web GET $subscriptionPage 200
    $decoded = [Net.WebUtility]::HtmlDecode($page.Body)
    Assert-Check ($decoded -match 'Deneme dönemi etkin' -and $decoded -match 'UTC\+03:00' -and $decoded -match '30 gün') 'UI clearly shows trial, Istanbul time and thirty-day policy'
    Assert-Check ($page.Headers.CacheControl.NoStore -and $page.Headers.CacheControl.Private) 'Web subscription page is private/no-store'
    Assert-Check ($page.Body -notmatch 'access_token|localStorage|Bearer ' -and $page.Body -notmatch '<form[^>]+action="/Subscription') 'UI must not expose bearer tokens or subscription mutation forms'
    Save-SubscriptionHtml $page 'subscription-trial.html'
    $refresh = Web GET $subscriptionPage 200
    $after = Read-Subscription
    Assert-Check ($after.trialStartedAtUtc -eq $trial.trialStartedAtUtc -and $after.trialEndsAtUtc -eq $trial.trialEndsAtUtc) 'Refreshing cannot extend or restart the trial'
    Web GET '/Subscription' 400 | Out-Null
    Web GET "/Subscription?tenantId=$([Guid]::NewGuid())" 404 | Out-Null
    Logout-Web $page
    Login-Web $translator.Email $translator.Password
    $page = Web GET $subscriptionPage 200
    Web GET "/Subscription?tenantId=$($other.id)" 404 | Out-Null
    Active $translatorId $false | Out-Null
    Web GET $subscriptionPage 404 | Out-Null
    Active $translatorId $true | Out-Null
    $page = Web GET $subscriptionPage 200
    Logout-Web $page
    Login-Web $guest.Email $guest.Password
    Web GET $subscriptionPage 404 | Out-Null
    $workspace = Web GET '/Workspace' 200
    Logout-Web $workspace
    Login-Web $email $password
    if ($testSubscriptionExpiredFixture) {
        # Optional native SQL setup, scoped ONLY to the independent tenant created by this run.
        # Production services still never read or write each other's database.
        $fixtureGuid = [Guid]::Parse($other.id)
        $query = "UPDATE billing.subscriptions SET TrialStartedAtUtc = DATEADD(day,-31,SYSUTCDATETIME()), TrialEndsAtUtc = DATEADD(day,-1,SYSUTCDATETIME()) WHERE TenantId = '$fixtureGuid' AND PlanCode = 'trial'; IF @@ROWCOUNT <> 1 THROW 51000, 'Expected exactly one development fixture subscription', 1;"
        & sqlcmd -S '.\ESIMSSQLSERVER' -d ETranslateBilling -E -C -b -Q $query
        if ($LASTEXITCODE -ne 0) { throw 'Expired fixture SQL setup failed' }
        $expired = Api GET "$subscriptionBillingUrl/api/v1/tenants/$fixtureGuid/subscription" $null 200
        Assert-Check ($expired.status -eq 'Expired' -and $expired.remainingTrialDays -eq 0) 'API must observe expiry even before worker persistence'
        $expiredPage = Web GET "/Subscription?tenantId=$fixtureGuid" 200
        $decoded = [Net.WebUtility]::HtmlDecode($expiredPage.Body)
        Assert-Check ($decoded -match 'Süresi doldu' -and $decoded -match 'salt okunur') 'Expired UI must show enforcement without deleting documents'
        Save-SubscriptionHtml $expiredPage 'subscription-expired.html'
        Api POST "$WorkflowUrl/api/v1/tenants/$fixtureGuid/translation-jobs" @{ title = 'Expired trial read-only slice check'; sourceLanguageCode = 'fa'; targetLanguageCode = 'tr'; notaryRequirement = 'NotRequired' } 402 | Out-Null
    }
    $page = Web GET $subscriptionPage 200
    Logout-Web $page
    Web GET $subscriptionPage 302 | Out-Null
    Write-Output "PASS: $($checks - $subscriptionPriorChecks) subscription checks plus $subscriptionPriorChecks baseline/team checks. Development tenant: $tenantId"
    if ($emitSubscriptionFixture) { Write-Output (ConvertTo-Json @{ email = $email; password = $password; tenantId = $tenantId; independentTenantId = $other.id } -Compress) }
} finally { $client.Dispose(); $handler.Dispose() }
