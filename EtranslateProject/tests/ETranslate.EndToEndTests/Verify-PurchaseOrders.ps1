#Requires -Version 7.0
param([string]$WebUrl='http://localhost:5067', [string]$IdentityUrl='http://localhost:5031',
    [string]$WorkflowUrl='http://localhost:5285', [string]$DocumentsUrl='http://localhost:5241',
    [string]$TrustUrl='http://localhost:5033', [string]$BillingUrl='http://localhost:5249')
$ErrorActionPreference='Stop'
# Baseline validates loopback URLs and creates isolated synthetic tenants/accounts.
$orderBillingUrl=$BillingUrl
. "$PSScriptRoot/Verify-PaidPeriods.ps1" -WebUrl $WebUrl -IdentityUrl $IdentityUrl -WorkflowUrl $WorkflowUrl -DocumentsUrl $DocumentsUrl -TrustUrl $TrustUrl -BillingUrl $BillingUrl
$orderBaselineChecks=$checks
$handler=[Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect=$false
$client=[Net.Http.HttpClient]::new($handler); $client.Timeout=[TimeSpan]::FromSeconds(45)
$orderOldSqlFlag=$env:ETRANSLATE_BILLING_SQL_TESTS
try {
    $ordersApi="$orderBillingUrl/api/v1/tenants/$tenantId/subscription/orders"
    $subscriptionApi="$orderBillingUrl/api/v1/tenants/$tenantId/subscription"
    $before=Api GET $subscriptionApi $null 200
    Api GET $ordersApi $null 401 @{} | Out-Null
    Api GET $ordersApi $null 401 @{ Authorization='Bearer invalid' } | Out-Null
    Api GET $ordersApi $null 401 @{ 'X-Tenant-Id'=$tenantId; 'X-Role'='Owner' } | Out-Null
    foreach ($account in @($guest,$translator,$observer)) {
        Api GET $ordersApi $null 403 $account.Headers | Out-Null
        Api POST $ordersApi @{planCode='office-monthly'; requestId=[Guid]::NewGuid()} 403 $account.Headers | Out-Null
        Api POST "$ordersApi/$([Guid]::NewGuid())/cancel" @{expectedVersion=0} 403 $account.Headers | Out-Null
    }
    $nonce=[Guid]::NewGuid()
    $draftBody=@{planCode='office-monthly'; requestId=$nonce}
    $order=Api POST $ordersApi $draftBody 201
    Assert-Check ($order.status -eq 'Draft' -and $order.currency -eq 'TRY' -and $null -eq $order.amountTry -and -not $order.checkoutEnabled -and -not $order.paymentReceived) 'Unpriced TRY draft is not free, paid, or checkout-enabled'
    Assert-Check (([DateTimeOffset]$order.expiresAtUtc)-([DateTimeOffset]$order.createdAtUtc) -eq [TimeSpan]::FromMinutes(30)) 'Draft has fixed thirty-minute lifetime'
    Assert-Check ($order.intent -eq 'Renewal' -and $before.status -eq 'Expired') 'Expired paid workspace can request renewal without access activation'
    $replay=Api POST $ordersApi $draftBody 200
    Assert-Check ($replay.id -eq $order.id -and $replay.expiresAtUtc -eq $order.expiresAtUtc) 'Same nonce replays without extending the draft'
    Api POST $ordersApi $draftBody 409 $admin.Headers | Out-Null
    Api POST $ordersApi @{planCode='independent-monthly'; requestId=$nonce} 409 | Out-Null
    Api POST $ordersApi @{planCode='independent-monthly'; requestId=[Guid]::NewGuid()} 409 | Out-Null
    Api POST $ordersApi @{planCode='missing'; requestId=[Guid]::NewGuid()} 409 | Out-Null
    Api POST $ordersApi @{planCode='office-monthly'; requestId=[Guid]::Empty} 400 | Out-Null
    foreach ($forged in @(@{amountTry=0},@{currency='USD'},@{status='Paid'},@{paymentReference='forged'})) {
        $body=@{planCode='office-monthly';requestId=[Guid]::NewGuid()}
        foreach ($key in $forged.Keys) { $body[$key]=$forged[$key] }
        Api POST $ordersApi $body 400 | Out-Null
    }
    $soloApi="$orderBillingUrl/api/v1/tenants/$($other.id)/subscription/orders"
    Api GET "$soloApi/$($order.id)" $null 404 | Out-Null
    Api POST "$soloApi/$($order.id)/cancel" @{expectedVersion=0} 404 | Out-Null
    $solo=Api POST $soloApi @{planCode='independent-monthly';requestId=$nonce} 201
    Assert-Check ($solo.id -ne $order.id -and $solo.tenantId -eq $other.id -and $solo.intent -eq 'Purchase') 'Idempotency is tenant-scoped'
    Api GET $soloApi $null 403 $admin.Headers | Out-Null
    Role $adminId 'Reviewer' | Out-Null
    Api GET $ordersApi $null 403 $admin.Headers | Out-Null
    Api POST $ordersApi @{planCode='office-monthly';requestId=[Guid]::NewGuid()} 403 $admin.Headers | Out-Null
    Role $adminId 'Administrator' | Out-Null
    Active $adminId $false | Out-Null
    Api GET $ordersApi $null 403 $admin.Headers | Out-Null
    Active $adminId $true | Out-Null
    Api GET $ordersApi $null 200 $admin.Headers | Out-Null
    Api POST "$ordersApi/$($order.id)/cancel" @{expectedVersion=1} 409 | Out-Null
    $canceled=Api POST "$ordersApi/$($order.id)/cancel" @{expectedVersion=0} 200 $admin.Headers
    Assert-Check ($canceled.status -eq 'Canceled' -and $canceled.version -eq 1) 'Authorized manager can cancel only the draft'
    $again=Api POST "$ordersApi/$($order.id)/cancel" @{expectedVersion=0} 200
    Assert-Check ($again.canceledAtUtc -eq $canceled.canceledAtUtc) 'Repeated cancellation has no second mutation'
    $replay=Api POST $ordersApi $draftBody 200
    Assert-Check ($replay.status -eq 'Canceled' -and $replay.id -eq $order.id) 'Canceled nonce cannot create a replacement'
    # Concurrent HTTP retries share a nonce; they must all converge to one immutable order.
    $requests=@(); $tasks=@(); $raceOrders=@(); $raceStatuses=@()
    try {
        $raceBody=@{planCode='office-monthly';requestId=[Guid]::NewGuid()}
        for ($i=0; $i -lt 6; $i++) {
            $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,$ordersApi)
            $request.Headers.TryAddWithoutValidation('Authorization',$bearer.Authorization) | Out-Null
            $request.Content=[Net.Http.StringContent]::new((ConvertTo-Json $raceBody),[Text.Encoding]::UTF8,'application/json')
            $requests+=$request; $tasks+=$client.SendAsync($request)
        }
        foreach ($task in $tasks) {
            $response=$task.GetAwaiter().GetResult()
            try {
                $raceStatuses += [int]$response.StatusCode
                $raceOrders += ConvertFrom-Json ($response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
            } finally { $response.Dispose() }
        }
        Assert-Check (@($raceStatuses | Where-Object {$_ -eq 201}).Count -eq 1 -and @($raceStatuses | Where-Object {$_ -eq 200}).Count -eq 5) 'Exactly one parallel retry creates the order'
        Assert-Check (@($raceOrders.id | Select-Object -Unique).Count -eq 1) 'Parallel retry IDs converge'
    } finally { foreach ($request in $requests) { $request.Dispose() } }
    # Synthetic clock fixture ONLY: validated IDs belong to this fresh run, no system clock/history changes.
    $expiredId=[Guid]::Parse($raceOrders[0].id); $fixtureTenant=[Guid]::Parse($tenantId)
    $query="DECLARE @now datetimeoffset=SYSUTCDATETIME(); UPDATE billing.purchase_orders SET CreatedAtUtc=DATEADD(minute,-31,@now), ExpiresAtUtc=DATEADD(minute,-1,@now) WHERE Id='$expiredId' AND TenantId='$fixtureTenant' AND Status='Draft'; IF @@ROWCOUNT<>1 THROW 51000,'Expected one newly-created synthetic order',1;"
    & sqlcmd -S '.\ESIMSSQLSERVER' -d ETranslateBilling -E -C -b -Q $query | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic order clock setup failed' }
    $expired=Api GET "$ordersApi/$expiredId" $null 200
    Assert-Check ($expired.status -eq 'Expired') 'Expiry is observed without a background worker'
    Api POST "$ordersApi/$expiredId/cancel" @{expectedVersion=0} 409 | Out-Null
    $replay=Api POST $ordersApi $raceBody 200
    Assert-Check ($replay.status -eq 'Expired' -and $replay.expiresAtUtc -eq $expired.expiresAtUtc) 'Expired replay cannot extend its deadline'
    Web GET "/PurchaseOrders?tenantId=$tenantId" 302 | Out-Null
    $loginPage=Web GET '/Account/Login' 200
    Web POST '/Account/Login' 302 (Fields @{email=$email; password=$password; __RequestVerificationToken=(Csrf $loginPage.Body)}) | Out-Null
    $page=Web GET "/PurchaseOrders?tenantId=$tenantId" 200
    $text=[Net.WebUtility]::HtmlDecode($page.Body)
    Assert-Check ($text -match 'Ödeme yapmadan ön sipariş' -and $text -match 'Fiyat henüz belirlenmedi' -and $text -match 'TRY' -and $text -match 'Süresi doldu') 'UI shows unpriced TRY drafts and expired history'
    Assert-Check ($page.Body -notmatch 'access_token|localStorage|Bearer |paymentReference' -and $page.Headers.CacheControl.ToString() -match 'no-store') 'Private order HTML contains no server tokens'
    $form=@{tenantId=$tenantId;planCode='office-monthly';requestId=[Guid]::NewGuid()}
    Web POST '/PurchaseOrders/Create' 400 (Fields $form) | Out-Null
    $form.__RequestVerificationToken=Csrf $page.Body
    Web POST '/PurchaseOrders/Create' 302 (Fields $form) | Out-Null
    $created=Api POST $ordersApi @{planCode='office-monthly';requestId=$form.requestId} 200
    $page=Web GET "/PurchaseOrders?tenantId=$tenantId" 200
    Assert-Check ([Net.WebUtility]::HtmlDecode($page.Body) -match 'ödeme yapılmadı') 'BFF notice never claims payment'
    $cancelForm=@{tenantId=$tenantId;orderId=$created.id;expectedVersion=0}
    Web POST '/PurchaseOrders/Cancel' 400 (Fields $cancelForm) | Out-Null
    $cancelForm.__RequestVerificationToken=Csrf $page.Body
    Web POST '/PurchaseOrders/Cancel' 302 (Fields $cancelForm) | Out-Null
    Assert-Check ((Api GET "$ordersApi/$($created.id)" $null 200).status -eq 'Canceled') 'BFF cancellation persists only order status'
    $after=Api GET $subscriptionApi $null 200
    Assert-Check ($after.status -eq $before.status -and $after.paidStartsAtUtc -eq $before.paidStartsAtUtc -and $after.paidEndsAtUtc -eq $before.paidEndsAtUtc -and $after.trialEndsAtUtc -eq $before.trialEndsAtUtc) 'All draft operations preserve subscription dates and status'
    Assert-Check (-not (Decision).canWrite) 'Drafts never unlock business writes'
    $client.Dispose(); $handler.Dispose()
    $handler=[Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect=$false
    $client=[Net.Http.HttpClient]::new($handler); $client.Timeout=[TimeSpan]::FromSeconds(45)
    $loginPage=Web GET '/Account/Login' 200
    Web POST '/Account/Login' 302 (Fields @{email=$translator.Email;password=$translator.Password;__RequestVerificationToken=(Csrf $loginPage.Body)}) | Out-Null
    Web GET "/PurchaseOrders?tenantId=$tenantId" 403 | Out-Null
    $env:ETRANSLATE_BILLING_SQL_TESTS='1'
    & dotnet test (Join-Path $PSScriptRoot '../ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-build --no-restore --filter 'FullyQualifiedName~PurchaseOrderSqlTests' -v quiet
    Assert-Check ($LASTEXITCODE -eq 0) 'Four native SQL order snapshot/isolation/concurrency tests pass'
    Write-Output "PASS: $($checks-$orderBaselineChecks) purchase-order checks plus $orderBaselineChecks baseline checks. Retained synthetic tenant: $tenantId"
} finally {
    $env:ETRANSLATE_BILLING_SQL_TESTS=$orderOldSqlFlag
    $client.Dispose(); $handler.Dispose()
}
