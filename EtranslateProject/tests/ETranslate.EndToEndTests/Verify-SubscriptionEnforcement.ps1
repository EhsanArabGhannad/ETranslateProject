#Requires -Version 7.0
param(
    [string]$WebUrl = 'http://localhost:5067',
    [string]$IdentityUrl = 'http://localhost:5031',
    [string]$WorkflowUrl = 'http://localhost:5285',
    [string]$DocumentsUrl = 'http://localhost:5241',
    [string]$TrustUrl = 'http://localhost:5033',
    [string]$BillingUrl = 'http://localhost:5249',
    [switch]$EmitTestFixture
)
$ErrorActionPreference = 'Stop'
# The SQL clock/status fixture setup must never target a remotely hosted environment.
foreach ($localUrl in @($WebUrl, $IdentityUrl, $WorkflowUrl, $DocumentsUrl, $TrustUrl, $BillingUrl)) {
    $localUri = [Uri]$localUrl
    if (-not $localUri.IsAbsoluteUri -or -not $localUri.IsLoopback -or $localUri.Scheme -notin @('http', 'https')) {
        throw 'Subscription enforcement fixtures require local HTTP/HTTPS service URLs.'
    }
}
$enforcementBillingUrl = $BillingUrl; $emitEnforcementFixture = $EmitTestFixture
. "$PSScriptRoot/Verify-SigningPreviews.ps1" -WebUrl $WebUrl -IdentityUrl $IdentityUrl -WorkflowUrl $WorkflowUrl -DocumentsUrl $DocumentsUrl -TrustUrl $TrustUrl -BillingUrl $BillingUrl
$enforcementPriorChecks = $checks
$handler = [Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect = $false
$client = [Net.Http.HttpClient]::new($handler); $client.Timeout = [TimeSpan]::FromSeconds(45)
$http = [Net.Http.HttpClient]::new(); $http.Timeout = [TimeSpan]::FromSeconds(45)
function Set-SubscriptionFixture([Guid]$Id, [int]$Status, [switch]$Fresh) {
    if ($Id -notin @([Guid]$tenantId, [Guid]$other.id) -or $Status -notin @(1,2,3,4,5)) { throw 'Invalid retained synthetic fixture' }
    $startDays = if ($Fresh) { -1 } else { -31 }; $endDays = if ($Fresh) { 29 } else { -1 }
    $query = "DECLARE @fixtureNow datetimeoffset = SYSUTCDATETIME(); UPDATE billing.subscriptions SET Status=$Status, TrialStartedAtUtc=DATEADD(day,$startDays,@fixtureNow), TrialEndsAtUtc=DATEADD(day,$endDays,@fixtureNow) WHERE TenantId='$Id' AND PlanCode='trial'; IF @@ROWCOUNT<>1 THROW 51000, 'Expected one synthetic trial fixture', 1;"
    & sqlcmd -S '.\ESIMSSQLSERVER' -d ETranslateBilling -E -C -b -Q $query | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Local subscription fixture setup failed' }
}
function Decision([string]$Id = $tenantId, $Headers = $bearer, [int]$Status = 200) {
    Api GET "$enforcementBillingUrl/api/v1/tenants/$Id/subscription/write-access" $null $Status $Headers
}
function Blocked-Upload([string]$Url) {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, $Url)
    $request.Headers.TryAddWithoutValidation('Authorization', $bearer.Authorization) | Out-Null
    $form = [Net.Http.MultipartFormDataContent]::new()
    $content = [Net.Http.ByteArrayContent]::new([Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jf1sAAAAASUVORK5CYII='))
    $content.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('image/png')
    $form.Add($content, 'file', 'fixture.png'); $request.Content = $form
    try {
        $response = $http.SendAsync($request).GetAwaiter().GetResult()
        try {
            Assert-Check ([int]$response.StatusCode -eq 402) 'Multipart mutations must enforce subscriptions before storing files'
            $problem = ConvertFrom-Json ($response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
            Assert-Check ($problem.code -eq 'subscription_required') 'Multipart denial must use a safe subscription code'
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}
try {
    Decision $tenantId @{} 401 | Out-Null
    Decision $tenantId $guest.Headers 403 | Out-Null
    Decision $tenantId @{ Authorization = 'Bearer invalid-fixture' } 401 | Out-Null
    Decision $tenantId @{ 'X-Tenant-Id' = $tenantId } 401 | Out-Null
    Decision ([Guid]::NewGuid().ToString()) $bearer 403 | Out-Null
    $allowed = Decision
    Assert-Check ($allowed.canWrite -and $allowed.status -eq 'Trialing' -and $null -eq $allowed.reason) 'Valid trial allows operations without claiming a payment'
    $raw = Invoke-WebRequest "$enforcementBillingUrl/api/v1/tenants/$tenantId/subscription/write-access" -Headers $bearer
    Assert-Check ($raw.Headers['Cache-Control'] -match 'private' -and $raw.Headers['Cache-Control'] -match 'no-store') 'Entitlement observations must not be cached'
    $documentReadUrl = "$DocumentsUrl/api/v1/tenants/$tenantId/translation-jobs/$jobId/documents"
    $beforeDocument = Api GET $documentReadUrl $null 200
    $beforeHistory = @(Api GET "$reviewApi/draft-revisions" $null 200)
    $beforePdfs = @(Api GET "$reviewApi/pdfs" $null 200)
    $beforePreviews = @(Api GET "$reviewApi/signing-previews" $null 200)
    $beforeJobs = @(Api GET "$WorkflowUrl/api/v1/tenants/$tenantId/translation-jobs" $null 200)
    $beforeTemplates = @(Api GET "$DocumentsUrl/api/v1/tenants/$tenantId/document-templates" $null 200)
    $archived = Pdf-Bytes "$reviewApi/pdfs/$($sampleSource.id)"
    $archivedPreview = Pdf-Bytes "$reviewApi/signing-previews/$($preview.id)/file"
    Set-SubscriptionFixture ([Guid]$tenantId) 1
    $denied = Decision
    Assert-Check (-not $denied.canWrite -and $denied.status -eq 'Expired' -and $denied.reason -eq 'subscription_required') 'Expiry is observed without depending on worker persistence'
    foreach ($member in @($bearer, $admin.Headers, $translator.Headers, $observer.Headers)) {
        Assert-Check (-not (Decision $tenantId $member).canWrite) 'No member or manager bypasses tenant subscription expiry'
    }
    $jobsPath = "$WorkflowUrl/api/v1/tenants/$tenantId/translation-jobs"
    $templatesPath = "$DocumentsUrl/api/v1/tenants/$tenantId/document-templates"
    $writes = @(
        @{ Method='POST'; Url=$jobsPath; Body=@{ title='Must not exist'; sourceLanguageCode='fa'; targetLanguageCode='tr'; notaryRequirement='NotRequired' } },
        @{ Method='PUT'; Url="$jobsPath/$jobId"; Body=@{ title='Must not update'; sourceLanguageCode='fa'; targetLanguageCode='tr'; notaryRequirement='NotRequired' } },
        @{ Method='PUT'; Url="$jobsPath/$jobId/assignment"; Body=@{ translatorUserId=$translatorId; expectedAssignmentVersion=0 } },
        @{ Method='POST'; Url=$templatesPath; Body=@{} },
        @{ Method='POST'; Url="$templatesPath/$templateId/revisions"; Body=@{} },
        @{ Method='PUT'; Url="$templatesPath/$templateId/status"; Body=@{ isActive=$false } },
        @{ Method='POST'; Url="$DocumentsUrl/api/v1/tenants/$tenantId/translation-jobs/$jobId/documents"; Body=$null },
        @{ Method='POST'; Url="$reviewApi/template"; Body=@{ templateId=$templateId; revisionNumber=1 } },
        @{ Method='POST'; Url="$reviewApi/draft-revisions"; Body=@{ expectedCurrentRevision=5; editorContentJson='{"type":"doc","content":[]}' } },
        @{ Method='POST'; Url="$reviewApi/draft-revisions/5/pdfs"; Body=$null },
        @{ Method='POST'; Url="$reviewApi/reviews"; Body=@{ expectedRevision=5; expectedRound=3; pdfVersionId=$sampleSource.id } },
        @{ Method='POST'; Url="$reviewApi/reviews/$($sampleReview.id)/decision"; Body=@{ action='Reopen'; note='Must not reopen' } },
        @{ Method='POST'; Url=$preparationApi; Body=$sampleProposal },
        @{ Method='POST'; Url="$preparationApi/$($samplePlan.id)/cancel"; Body=@{ expectedVersion=0; reason='Must not cancel' } },
        @{ Method='POST'; Url="$preparationApi/$($samplePlan.id)/dispatch"; Body=$null },
        @{ Method='POST'; Url="$reviewApi/signing-preparations/$($samplePlan.id)/preview-pdfs"; Body=$null }
    )
    foreach ($write in $writes) {
        $problem = Api $write.Method $write.Url $write.Body 402
        Assert-Check ($problem.code -eq 'subscription_required') 'Every write group must deny through the subscription filter'
    }
    Blocked-Upload "$reviewApi/source-files"
    Blocked-Upload "$templatesPath/$templateId/assets"
    Api POST $jobsPath $writes[0].Body 401 @{} | Out-Null
    Api POST $jobsPath $writes[0].Body 403 $guest.Headers | Out-Null
    Active $translatorId $false | Out-Null
    Api POST $jobsPath $writes[0].Body 403 $translator.Headers | Out-Null
    Api GET $documentReadUrl $null 403 $translator.Headers | Out-Null
    Active $translatorId $true | Out-Null
    $afterDocument = Api GET $documentReadUrl $null 200
    Assert-Check ((ConvertTo-Json $beforeDocument -Depth 10 -Compress) -eq (ConvertTo-Json $afterDocument -Depth 10 -Compress)) 'Denied document writes must leave metadata and source files unchanged'
    Assert-Check (@(Api GET "$reviewApi/draft-revisions" $null 200).Count -eq $beforeHistory.Count) 'No rejected draft revision can be stored'
    Assert-Check (@(Api GET "$reviewApi/pdfs" $null 200).Count -eq $beforePdfs.Count) 'No rejected PDF can be stored'
    Assert-Check (@(Api GET "$reviewApi/signing-previews" $null 200).Count -eq $beforePreviews.Count) 'No rejected preview can be stored'
    Assert-Check (@(Api GET $jobsPath $null 200).Count -eq $beforeJobs.Count) 'Rejected job writes leave the list unchanged'
    Assert-Check (@(Api GET $templatesPath $null 200).Count -eq $beforeTemplates.Count) 'Rejected template writes leave the list unchanged'
    $state = Api GET $preparationApi $null 200
    Assert-Check (@($state.preparations | Where-Object id -eq $samplePlan.id)[0].status -eq 'Prepared') 'Rejected cancellation preserves the plan'
    Assert-Check ((Hash-Bytes (Pdf-Bytes "$reviewApi/pdfs/$($sampleSource.id)" $translator.Headers).Bytes) -eq (Hash-Bytes $archived.Bytes)) 'Archived translation remains byte-identical and downloadable after expiry'
    Assert-Check ((Hash-Bytes (Pdf-Bytes "$reviewApi/signing-previews/$($preview.id)/file" $observer.Headers).Bytes) -eq (Hash-Bytes $archivedPreview.Bytes)) 'Historical unsigned preview remains downloadable after expiry'
    foreach ($source in $beforeDocument.sourceFiles) {
        $file = Invoke-WebRequest "$reviewApi/source-files/$($source.id)" -Headers $bearer
        Assert-Check ($file.StatusCode -eq 200) 'Source files must remain readable after expiry'
    }
    $loginPage = Web GET '/Account/Login' 200
    Web POST '/Account/Login' 302 (Fields @{ email=$email; password=$password; __RequestVerificationToken=(Csrf $loginPage.Body) }) | Out-Null
    $workspace = Web GET "/Workspace?tenantId=$tenantId" 200
    Assert-Check ($workspace.Body -match 'data-subscription-readonly="true"' -and $workspace.Body -match 'subscription-lock" disabled') 'Workspace exposes readonly mode while retaining navigation'
    Web GET "/Team?tenantId=$tenantId" 200 | Out-Null
    $editor = Web GET "/Workspace/Editor?tenantId=$tenantId&jobId=$jobId" 200
    Assert-Check ($editor.Body -match 'data-editable="false"' -and $editor.Body -match '/Workspace/DraftPdf') 'Editor is noneditable but archived download links remain'
    $unsavedText = 'این متن آزمایشی ذخیره نشده است؛ باید حفظ شود.'
    $unsavedJson = ConvertTo-Json @{ type='doc'; content=@(@{ type='paragraph'; content=@(@{ type='text'; text=$unsavedText }) }) } -Depth 8 -Compress
    $rejected = Web POST '/Workspace/SaveDraft' 402 (Fields @{ tenantId=$tenantId; jobId=$jobId; expectedRevision=5; mode='rich'; editorContentJson=$unsavedJson; __RequestVerificationToken=(Csrf $editor.Body) })
    Assert-Check ([Net.WebUtility]::HtmlDecode($rejected.Body).Contains($unsavedText)) 'Rejected save must retain text for copying'
    Assert-Check ($rejected.Body -match '<textarea id="translation-text"[^>]*readonly' -and $rejected.Body -notmatch '<fieldset class="subscription-lock" disabled') 'Preserved translation text must remain focusable/copyable, not inside a disabled fieldset'
    $pdfRequest = Fields @{ tenantId=$tenantId; jobId=$jobId; revisionNumber=5; __RequestVerificationToken=(Csrf $editor.Body) }
    $jsonRequest = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, "$WebUrl/Workspace/GenerateDraftPdf")
    $jsonRequest.Headers.Accept.ParseAdd('application/json'); $jsonRequest.Content=$pdfRequest
    try {
        $response=$client.SendAsync($jsonRequest).GetAwaiter().GetResult()
        try { Assert-Check ([int]$response.StatusCode -eq 402) 'JSON PDF requests preserve subscription rejection status' }
        finally { $response.Dispose() }
    } finally { $jsonRequest.Dispose() }
    $page = Web GET "/Workspace/SigningPreparation?tenantId=$tenantId&jobId=$jobId" 200
    Assert-Check ($page.Body -notmatch '<form[^>]+action="/Workspace/(PrepareSigning|CancelSigningPreparation|GenerateSigningPreview)' -and $page.Body -match '/Workspace/SigningPreviewPdf') 'Signing history is readable without mutation controls'
    $rejectedProposal = Web POST '/Workspace/PrepareSigning' 402 (Fields @{ tenantId=$tenantId; jobId=$jobId; 'Form.ExpectedReviewId'=$sampleReview.id; 'Form.ExpectedPdfVersionId'=$sampleSource.id; 'Form.TranslatorUserId'=$translatorId; 'Form.TranslatorStatement'=$unsavedText; 'Form.TranslatorMethod'='EImza'; __RequestVerificationToken=(Csrf $page.Body) })
    Assert-Check ([Net.WebUtility]::HtmlDecode($rejectedProposal.Body).Contains($unsavedText)) 'Rejected proposed declaration must remain readable and unaccepted'
    $page = Web GET "/Workspace/Assignment?tenantId=$tenantId&jobId=$jobId" 200
    Assert-Check ($page.Body -notmatch '<form[^>]+action="/Workspace/AssignTranslator') 'Assignment history remains readable without assignment controls'
    Web GET "/Subscription?tenantId=$tenantId" 200 | Out-Null
    Set-SubscriptionFixture ([Guid]$other.id) 1
    $soloDecision = Decision $other.id
    Assert-Check (-not $soloDecision.canWrite -and $soloDecision.status -eq 'Expired') 'Independent tenants obey the same expiry policy'
    Api POST "$WorkflowUrl/api/v1/tenants/$($other.id)/translation-jobs" $writes[0].Body 402 | Out-Null
    Api GET $independentDocuments $null 200 | Out-Null
    foreach ($status in @(3,4)) {
        Set-SubscriptionFixture ([Guid]$tenantId) $status
        Assert-Check (-not (Decision).canWrite) 'PastDue and Canceled cannot write'
        Api POST $jobsPath $writes[0].Body 402 | Out-Null
    }
    # Synthetic status fixture ONLY: not a payment or production activation path.
    Set-SubscriptionFixture ([Guid]$tenantId) 2
    Assert-Check ((Decision).canWrite) 'Trusted Active state permits writes; no payment is fabricated by the product'
    Api POST $jobsPath @{ title='Synthetic Active fixture'; sourceLanguageCode='fa'; targetLanguageCode='tr'; notaryRequirement='NotRequired' } 201 | Out-Null
    Set-SubscriptionFixture ([Guid]$tenantId) 1 -Fresh
    Assert-Check ((Decision).canWrite) 'Restored synthetic valid trial recovers writes without a cached denial'
    Api POST $jobsPath @{ title='Restored synthetic trial'; sourceLanguageCode='fa'; targetLanguageCode='tr'; notaryRequirement='NotRequired' } 201 | Out-Null
    Set-SubscriptionFixture ([Guid]$tenantId) 1
    Assert-Check (-not (Decision).canWrite) 'Re-expiry takes effect on the next check, without a cached grant'
    Write-Output "PASS: $($checks-$enforcementPriorChecks) enforcement checks plus $enforcementPriorChecks signing/Web/team baseline checks. Retained expired tenant: $tenantId"
    if ($emitEnforcementFixture) { Write-Output (ConvertTo-Json @{ email=$email; password=$password; tenantId=$tenantId; jobId=$jobId; documentId=$document.id; sourcePdfId=$sampleSource.id } -Compress) }
} finally { $http.Dispose(); $client.Dispose(); $handler.Dispose() }
