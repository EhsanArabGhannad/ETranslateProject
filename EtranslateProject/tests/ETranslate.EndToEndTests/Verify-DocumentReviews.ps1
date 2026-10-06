#Requires -Version 7.0
param(
    [string]$WebUrl = 'https://localhost:7049',
    [string]$IdentityUrl = 'http://localhost:5031',
    [string]$WorkflowUrl = 'http://localhost:5285',
    [string]$DocumentsUrl = 'http://localhost:5241',
    [switch]$EmitBrowserFixture
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Verify-WebWorkspace.ps1" -WebUrl $WebUrl -IdentityUrl $IdentityUrl -WorkflowUrl $WorkflowUrl -DocumentsUrl $DocumentsUrl
$baselineChecks = $checks
$handler = [Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect = $false
$client = [Net.Http.HttpClient]::new($handler); $client.Timeout = [TimeSpan]::FromSeconds(45)
$http = [Net.Http.HttpClient]::new(); $http.Timeout = [TimeSpan]::FromSeconds(45)
$reviewApi = "$documentPath/$($document.id)"
function Pdf([int]$Revision) { Api POST "$reviewApi/draft-revisions/$Revision/pdfs" $null 201 }
function Submit([int]$Revision, [int]$Round, [string]$PdfId, [int]$Status = 201) {
    Api POST "$reviewApi/reviews" @{ expectedRevision = $Revision; expectedRound = $Round; pdfVersionId = $PdfId } $Status
}
function Decide([string]$Id, [string]$Action, [string]$Note = '', [int]$Status = 200) {
    Api POST "$reviewApi/reviews/$Id/decision" @{ action = $Action; note = $Note } $Status
}
function Upload([int]$Expected) {
    $form = [Net.Http.MultipartFormDataContent]::new()
    $image = [Net.Http.ByteArrayContent]::new($png); $image.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('image/png')
    $form.Add($image, 'file', 'review-source.png')
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, "$reviewApi/source-files")
    $request.Headers.TryAddWithoutValidation('Authorization', $bearer.Authorization) | Out-Null; $request.Content = $form
    try {
        $response = $http.SendAsync($request).GetAwaiter().GetResult()
        try {
            Assert-Check ([int]$response.StatusCode -eq $Expected) "Review source upload expected $Expected, got $([int]$response.StatusCode)"
            if ($Expected -eq 201) { ConvertFrom-Json $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() }
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}
function Concurrent([string[]]$Paths, [object[]]$Bodies) {
    $requests = @(); $tasks = @()
    try {
        for ($index = 0; $index -lt $Paths.Count; $index++) {
            $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, $Paths[$index])
            $request.Headers.TryAddWithoutValidation('Authorization', $bearer.Authorization) | Out-Null
            if ($Bodies[$index] -is [Net.Http.HttpContent]) { $request.Content = $Bodies[$index] }
            else { $request.Content = [Net.Http.StringContent]::new((ConvertTo-Json -InputObject $Bodies[$index] -Depth 15), [Text.Encoding]::UTF8, 'application/json') }
            $requests += $request; $tasks += $http.SendAsync($request)
        }
        foreach ($task in $tasks) {
            $response = $task.GetAwaiter().GetResult()
            try { [int]$response.StatusCode } finally { $response.Dispose() }
        }
    } finally { foreach ($request in $requests) { $request.Dispose() } }
}
try {
    $loginPage = Web GET '/Account/Login' 200
    Web POST '/Account/Login' 302 (Fields @{ email = $email; password = $password; __RequestVerificationToken = (Csrf $loginPage.Body) }) | Out-Null
    $page = Web GET $editorPath 200; $csrf = Csrf $page.Body
    $source = Upload 201
    Api GET "$reviewApi/reviews" $null 401 @{} | Out-Null
    Api POST "$reviewApi/reviews" @{ expectedRevision = 2; expectedRound = 0; pdfVersionId = [Guid]::NewGuid() } 401 @{} | Out-Null
    $state = Api GET "$reviewApi/reviews" $null 200
    Assert-Check ($state.status -eq 'Draft' -and $state.canManage -and $state.canReview -and $state.reviews.Count -eq 0) 'Initial owner review permissions/state incorrect'
    Submit 2 0 ([Guid]::NewGuid()) 404 | Out-Null
    $oldPdf = Pdf 1; $pdf = Pdf 2
    Submit 1 0 $oldPdf.id 409 | Out-Null
    $values = @{ tenantId = $tenantId; jobId = $jobId; expectedRevision = 2; expectedRound = 0; pdfVersionId = $pdf.id }
    Web POST '/Workspace/SubmitReview' 400 (Fields $values) | Out-Null
    $values.__RequestVerificationToken = $csrf
    Web POST '/Workspace/SubmitReview' 302 (Fields $values) | Out-Null
    $state = Api GET "$reviewApi/reviews" $null 200; $first = $state.reviews[0]
    Api POST "$reviewApi/reviews/$($first.id)/decision" @{ action = 'Approve' } 401 @{} | Out-Null
    Assert-Check ($state.status -eq 'AwaitingReview' -and $first.round -eq 1 -and $first.revisionNumber -eq 2) 'Submission must lock exact current revision'
    Assert-Check ($first.pdfVersionId -eq $pdf.id -and $first.pdfSha256 -eq $pdf.sha256) 'Review must pin PDF identity/digest'
    Assert-Check ($first.sourceFiles.Count -eq 1 -and $first.sourceFiles[0].id -eq $source.id -and $first.sourceFiles[0].sha256 -eq $source.sha256) 'Submission must snapshot source identities/digests'
    $page = Web GET $editorPath 200
    Assert-Check ($page.Body -match 'data-editable="false"' -and $page.Body -notmatch 'id="source-upload-form"' -and $page.Body -match 'name="reviewAction" value="Approve"') 'Awaiting review page must lock editor/upload and expose owner decision'
    Api POST "$reviewApi/draft-revisions" @{ expectedCurrentRevision = 2; editorContentJson = '{"type":"doc","content":[]}' } 409 | Out-Null
    Upload 409 | Out-Null
    Submit 2 0 $pdf.id 409 | Out-Null
    $values.__RequestVerificationToken = Csrf $page.Body
    $decisionValues = @{ tenantId = $tenantId; jobId = $jobId; reviewId = $first.id; reviewAction = 'Approve' }
    Web POST '/Workspace/DecideReview' 400 (Fields $decisionValues) | Out-Null
    Decide $first.id 'Unknown' '' 400 | Out-Null
    Decide ([Guid]::NewGuid()) 'Approve' '' 404 | Out-Null
    Decide $first.id 'RequestChanges' '' 400 | Out-Null
    Decide $first.id 'RequestChanges' '<script>test</script> تاریخ را اصلاح کنید' | Out-Null
    Decide $first.id 'Approve' '' 409 | Out-Null
    Submit 2 1 $pdf.id 409 | Out-Null
    $page = Web GET $editorPath 200
    Assert-Check ($page.Body -match 'data-editable="true"' -and $page.Body -notmatch '<script>test</script>') 'Returned review must unlock editor and encode notes'
    Assert-Check ($page.Body -notmatch 'id="review-submit"') 'Returned review must require a new revision before showing resubmit controls'
    Api POST "$reviewApi/draft-revisions" @{ expectedCurrentRevision = 2; editorContentJson = '{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"اصلاح تاریخ - düzeltilmiş tarih"}]}]}' } 201 | Out-Null
    $pdf3 = Pdf 3
    $second = Submit 3 1 $pdf3.id
    Assert-Check ($second.round -eq 2 -and $second.id -ne $first.id) 'Resubmission must create a new review round'
    Decide $second.id 'Withdraw' '' 400 | Out-Null
    Decide $second.id 'Withdraw' 'برای بررسی دوباره' | Out-Null
    $third = Submit 3 2 $pdf3.id
    $page = Web GET $editorPath 200; $csrf = Csrf $page.Body
    $decisionValues.reviewId = $third.id; $decisionValues.__RequestVerificationToken = $csrf
    Web POST '/Workspace/DecideReview' 302 (Fields $decisionValues) | Out-Null
    $approved = Api GET "$reviewApi/reviews" $null 200
    Assert-Check ($approved.status -eq 'Approved' -and $approved.reviews.Count -eq 3 -and $approved.reviews[0].decidedByUserId -eq $approved.userId) 'Approval must retain reviewer and all previous rounds'
    Api POST "$reviewApi/draft-revisions" @{ expectedCurrentRevision = 3; editorContentJson = '{}' } 409 | Out-Null
    Upload 409 | Out-Null
    Decide $third.id 'Reopen' '' 400 | Out-Null
    $reopened = Decide $third.id 'Reopen' 'اصلاح بعد از تأیید'
    Assert-Check ($reopened.status -eq 'Reopened' -and $reopened.decidedAtUtc -eq $approved.reviews[0].decidedAtUtc -and $reopened.reopenedAtUtc) 'Reopen must preserve original approval audit'
    Submit 3 3 $pdf3.id 409 | Out-Null
    $newSource = Upload 201
    $oldState = Api GET "$reviewApi/reviews" $null 200
    Assert-Check ($oldState.reviews[-1].sourceFiles.Count -eq 1) 'New source must not change prior review snapshot'
    Api POST "$reviewApi/draft-revisions" @{ expectedCurrentRevision = 3; editorContentJson = '{"type":"doc","content":[]}' } 201 | Out-Null
    $pdf4 = Pdf 4
    # Two submissions of one revision must result in exactly one review.
    $submission = @{ expectedRevision = 4; expectedRound = 3; pdfVersionId = $pdf4.id }
    $statuses = @(Concurrent @("$reviewApi/reviews", "$reviewApi/reviews") @($submission, $submission)) | Sort-Object
    Assert-Check (($statuses -join ',') -eq '201,409') "Concurrent submissions must be 201/409, got $statuses"
    $state = Api GET "$reviewApi/reviews" $null 200; $fourth = $state.reviews[0]
    Assert-Check ($state.round -eq 4 -and $state.reviews.Count -eq 4 -and $fourth.sourceFiles.Count -eq 2) 'Only one new round and exact latest source set allowed'
    $statuses = @(Concurrent @("$reviewApi/reviews/$($fourth.id)/decision", "$reviewApi/reviews/$($fourth.id)/decision") @(
        @{ action = 'Approve'; note = 'approved' }, @{ action = 'RequestChanges'; note = 'fix' }
    )) | Sort-Object
    Assert-Check (($statuses -join ',') -eq '200,409') "Concurrent decisions must be 200/409, got $statuses"
    $state = Api GET "$reviewApi/reviews" $null 200
    if ($state.status -eq 'Approved') { Decide $fourth.id 'Reopen' 'Concurrency test correction' | Out-Null }
    Api POST "$reviewApi/draft-revisions" @{ expectedCurrentRevision = 4; editorContentJson = '{"type":"doc","content":[]}' } 201 | Out-Null
    $pdf5 = Pdf 5
    $statuses = @(Concurrent @("$reviewApi/reviews", "$reviewApi/draft-revisions") @(
        @{ expectedRevision = 5; expectedRound = 4; pdfVersionId = $pdf5.id }, @{ expectedCurrentRevision = 5; editorContentJson = '{"type":"doc","content":[]}' }
    )) | Sort-Object
    Assert-Check (($statuses -join ',') -eq '201,409') "Save versus submission must be 201/409, got $statuses"
    $state = Api GET "$reviewApi/reviews" $null 200
    if ($state.status -ne 'AwaitingReview') {
        $pdf6 = Pdf 6; Submit 6 4 $pdf6.id | Out-Null
    }
    $state = Api GET "$reviewApi/reviews" $null 200
    # Upload versus submit can both succeed only if the upload was included in the pinned source set.
    $pending = $state.reviews[0]
    Decide $pending.id 'Withdraw' 'Source upload concurrency test' | Out-Null
    $form = [Net.Http.MultipartFormDataContent]::new()
    $image = [Net.Http.ByteArrayContent]::new($png); $image.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('image/png')
    $form.Add($image, 'file', 'concurrent-source.png')
    $submitBody = @{ expectedRevision = $pending.revisionNumber; expectedRound = $pending.round; pdfVersionId = $pending.pdfVersionId }
    $statuses = @(Concurrent @("$reviewApi/reviews", "$reviewApi/source-files") @($submitBody, $form))
    Assert-Check ($statuses.Count -eq 2 -and @($statuses | Where-Object { $_ -notin 201,409 }).Count -eq 0 -and $statuses -contains 201) "Upload versus submit returned unexpected statuses: $statuses"
    if ($statuses[0] -eq 409) { Submit $pending.revisionNumber $pending.round $pending.pdfVersionId | Out-Null }
    $state = Api GET "$reviewApi/reviews" $null 200
    $current = Api GET $documentPath $null 200
    Assert-Check ($state.status -eq 'AwaitingReview' -and $state.reviews[0].sourceFiles.Count -eq $current.sourceFiles.Count) 'Frozen review must include every source that committed before its lock'
    Api GET "$DocumentsUrl/api/v1/tenants/$($other.id)/translation-jobs/$jobId/documents/$($document.id)/reviews" $null 404 | Out-Null
    Api GET "$DocumentsUrl/api/v1/tenants/$([Guid]::NewGuid())/translation-jobs/$jobId/documents/$($document.id)/reviews" $null 403 | Out-Null
    Api POST "$DocumentsUrl/api/v1/tenants/$($other.id)/translation-jobs/$jobId/documents/$($document.id)/reviews/$($state.reviews[0].id)/decision" @{ action = 'Approve' } 404 | Out-Null
    $independentPath = "$WorkflowUrl/api/v1/tenants/$($other.id)/translation-jobs"
    $independentJob = Api POST $independentPath @{ title = 'Independent internal review'; sourceLanguageCode = 'en'; targetLanguageCode = 'tr'; notaryRequirement = 'NotRequired' } 201
    $independentDocumentPath = "$DocumentsUrl/api/v1/tenants/$($other.id)/translation-jobs/$($independentJob.id)/documents"
    $independentDocument = Api POST $independentDocumentPath $null 201
    $independentApi = "$independentDocumentPath/$($independentDocument.id)"
    Api POST "$independentApi/draft-revisions" @{ expectedCurrentRevision = 0; editorContentJson = '{"type":"doc","content":[]}' } 201 | Out-Null
    $independentPdf = Api POST "$independentApi/draft-revisions/1/pdfs" $null 201
    Api POST "$independentApi/reviews" @{ expectedRevision = 1; expectedRound = 0; pdfVersionId = $pdf.id } 404 | Out-Null
    $independentReview = Api POST "$independentApi/reviews" @{ expectedRevision = 1; expectedRound = 0; pdfVersionId = $independentPdf.id } 201
    $independentApproval = Api POST "$independentApi/reviews/$($independentReview.id)/decision" @{ action = 'Approve' } 200
    Assert-Check ($independentApproval.status -eq 'Approved' -and $independentApproval.decidedByUserId -eq $independentApproval.submittedByUserId) 'Independent owner must be able to self-review internally'
    $page = Web GET $editorPath 200
    Assert-Check ($page.Body -match 'data-editable="false"') 'Final submitted fixture must be readonly'
    Web POST '/Account/Logout' 302 (Fields @{ __RequestVerificationToken = (Csrf $page.Body) }) | Out-Null
    Web GET $editorPath 302 | Out-Null
    Api GET "$reviewApi/reviews" $null 401 @{} | Out-Null
    Write-Output "PASS: $($checks - $baselineChecks) review checks plus $baselineChecks baseline checks. Development tenant: $tenantId"
    if ($EmitBrowserFixture) { Write-Output (ConvertTo-Json @{ email = $email; password = $password; tenantId = $tenantId; jobId = $jobId; editorPath = $editorPath } -Compress) }
} finally { $http.Dispose(); $client.Dispose(); $handler.Dispose() }
