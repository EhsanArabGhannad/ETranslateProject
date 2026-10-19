#Requires -Version 7.0
param([string]$WebUrl='http://localhost:5067', [string]$IdentityUrl='http://localhost:5031',
    [string]$WorkflowUrl='http://localhost:5285', [string]$DocumentsUrl='http://localhost:5241',
    [string]$TrustUrl='http://localhost:5033', [string]$BillingUrl='http://localhost:5249')
$ErrorActionPreference='Stop'
# The baseline validates loopback URLs and creates both synthetic tenants in this run.
$paidBillingUrl=$BillingUrl
. "$PSScriptRoot/Verify-SubscriptionEnforcement.ps1" -WebUrl $WebUrl -IdentityUrl $IdentityUrl -WorkflowUrl $WorkflowUrl -DocumentsUrl $DocumentsUrl -TrustUrl $TrustUrl -BillingUrl $BillingUrl
$paidPriorChecks=$checks
$oldSqlFlag=$env:ETRANSLATE_BILLING_SQL_TESTS; $oldUiFixture=$env:ETRANSLATE_PAID_UI_TENANT
$handler=[Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect=$false
$client=[Net.Http.HttpClient]::new($handler); $client.Timeout=[TimeSpan]::FromSeconds(45)
try {
    $plansApi="$paidBillingUrl/api/v1/tenants/$tenantId/subscription/plans"
    Api GET $plansApi $null 401 @{} | Out-Null
    Api GET $plansApi $null 403 $guest.Headers | Out-Null
    foreach ($member in @($bearer,$translator.Headers,$admin.Headers,$observer.Headers)) {
        $plans=Api GET $plansApi $null 200 $member
        Assert-Check ($plans.eligibilityKnown -and $plans.plans.Count -eq 1 -and $plans.plans[0].tenantType -eq 'TranslationOffice') 'Catalog is typed, scoped and readable by every active member'
        Assert-Check (-not $plans.purchaseEnabled -and -not $plans.pricingConfigured -and $plans.periodTimeZone -eq 'UTC') 'Catalog must not invent a price or offer unimplemented checkout'
    }
    $soloPlans=Api GET "$paidBillingUrl/api/v1/tenants/$($other.id)/subscription/plans" $null 200
    Assert-Check ($soloPlans.plans.Count -eq 1 -and $soloPlans.plans[0].code -eq 'independent-monthly') 'Independent workspace has its own eligible plan'
    Api POST "$paidBillingUrl/api/v1/tenants/$tenantId/subscription" @{ status='Active'; paymentReference='forged' } 405 | Out-Null
    Api POST "$paidBillingUrl/api/v1/tenants/$tenantId/subscription/plans" @{ code='office-monthly' } 405 | Out-Null
    Api POST "$paidBillingUrl/api/v1/tenants/$tenantId/subscription/activate" @{ paymentReference='forged' } 404 | Out-Null
    Api POST "$paidBillingUrl/api/v1/tenants/$tenantId/subscription/renew" @{ paymentReference='forged' } 404 | Out-Null
    $env:ETRANSLATE_BILLING_SQL_TESTS='1'; $env:ETRANSLATE_PAID_UI_TENANT=$tenantId
    $testProject=Join-Path $PSScriptRoot '../ETranslate.DomainTests/ETranslate.DomainTests.csproj'
    & dotnet test $testProject --no-build --no-restore --filter 'FullyQualifiedName~PaidPeriodSqlTests' -v quiet
    Assert-Check ($LASTEXITCODE -eq 0) 'Trusted in-process allocation and native SQL concurrency fixtures must pass'
    $snapshot=Api GET "$paidBillingUrl/api/v1/tenants/$tenantId/subscription" $null 200
    Assert-Check ($snapshot.status -eq 'Active' -and $snapshot.planCode -eq 'office-monthly' -and $snapshot.paidPlanCode -eq 'office-monthly') 'Applied synthetic period activates only the allocated plan'
    Assert-Check ($snapshot.paidMonths -eq 1 -and $snapshot.paidStartsAtUtc -and $snapshot.paidEndsAtUtc -and $null -eq $snapshot.remainingTrialDays) 'Paid dates are separate from trial dates'
    $decision=Decision
    Assert-Check ($decision.canWrite -and $decision.paidEndsAtUtc -eq $snapshot.paidEndsAtUtc) 'Active access has a bounded paid lease'
    $jobsPath="$WorkflowUrl/api/v1/tenants/$tenantId/translation-jobs"
    Api POST $jobsPath @{ title='Synthetic paid-period fixture'; sourceLanguageCode='fa'; targetLanguageCode='tr'; notaryRequirement='NotRequired' } 201 | Out-Null
    $login=Web GET '/Account/Login' 200
    Web POST '/Account/Login' 302 (Fields @{ email=$email; password=$password; __RequestVerificationToken=(Csrf $login.Body) }) | Out-Null
    $page=Web GET "/Subscription?tenantId=$tenantId" 200
    $text=[Net.WebUtility]::HtmlDecode($page.Body)
    Assert-Check ($text -match 'Ücretli erişim dönemi' -and $text -match 'takvim ayı' -and $text -match 'Ücretli erişim başlangıcı' -and $text -match 'Ücretli erişim sonu') 'BFF shows paid access dates and calendar policy without offering checkout'
    Assert-Check ($page.Body -notmatch 'PaymentReference|paymentReference|Bearer ') 'Private payment identities must never be displayed'
    $refresh=Api GET "$paidBillingUrl/api/v1/tenants/$tenantId/subscription" $null 200
    Assert-Check ($refresh.paidStartsAtUtc -eq $snapshot.paidStartsAtUtc -and $refresh.paidEndsAtUtc -eq $snapshot.paidEndsAtUtc) 'GET does not extend paid dates'
    # Synthetic clock setup ONLY. Do not change immutable allocation history or system time.
    $fixtureId=[Guid]::Parse($tenantId)
    $query="DECLARE @fixtureNow datetimeoffset=SYSUTCDATETIME(); UPDATE billing.subscriptions SET Status='Active', PaidStartsAtUtc=DATEADD(month,-2,@fixtureNow), BillingAnchorUtc=DATEADD(month,-2,@fixtureNow), PaidEndsAtUtc=DATEADD(month,1,DATEADD(month,-2,@fixtureNow)), PaidMonths=1, Version=Version+1 WHERE TenantId='$fixtureId' AND PlanCode='office-monthly'; IF @@ROWCOUNT<>1 THROW 51000,'Expected one newly-created synthetic paid fixture',1;"
    & sqlcmd -S '.\ESIMSSQLSERVER' -d ETranslateBilling -E -C -b -Q $query | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic paid expiry clock setup failed' }
    $expired=Api GET "$paidBillingUrl/api/v1/tenants/$tenantId/subscription" $null 200
    Assert-Check ($expired.status -eq 'Expired' -and $expired.remainingPaidDays -eq 0) 'Paid expiry is observed without waiting for the worker'
    Api POST $jobsPath @{ title='Must remain blocked'; sourceLanguageCode='fa'; targetLanguageCode='tr'; notaryRequirement='NotRequired' } 402 | Out-Null
    $page=Web GET "/Subscription?tenantId=$tenantId" 200
    Assert-Check ([Net.WebUtility]::HtmlDecode($page.Body) -match 'salt okunur') 'Expired paid page remains available and explains readonly status'
    Web GET "/Workspace?tenantId=$tenantId" 200 | Out-Null
    Web GET "/Workspace/Editor?tenantId=$tenantId&jobId=$jobId" 200 | Out-Null
    Write-Output "PASS: $($checks-$paidPriorChecks) paid-period HTTP checks plus $paidPriorChecks baseline checks. Retained expired synthetic paid tenant: $tenantId"
} finally {
    $env:ETRANSLATE_BILLING_SQL_TESTS=$oldSqlFlag; $env:ETRANSLATE_PAID_UI_TENANT=$oldUiFixture
    $client.Dispose(); $handler.Dispose()
}
