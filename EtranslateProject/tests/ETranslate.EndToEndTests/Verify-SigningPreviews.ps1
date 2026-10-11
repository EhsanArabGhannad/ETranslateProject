#Requires -Version 7.0
param(
    [string]$WebUrl = 'https://localhost:7049',
    [string]$IdentityUrl = 'http://localhost:5031',
    [string]$WorkflowUrl = 'http://localhost:5285',
    [string]$DocumentsUrl = 'http://localhost:5241',
    [string]$TrustUrl = 'http://localhost:5033',
    [string]$BillingUrl = 'http://localhost:5249',
    [switch]$EmitBrowserFixture,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$emitPreviewFixture = $EmitBrowserFixture; $previewOutputDirectory = $OutputDirectory
. "$PSScriptRoot/Verify-SigningPreparations.ps1" -WebUrl $WebUrl -IdentityUrl $IdentityUrl -WorkflowUrl $WorkflowUrl -DocumentsUrl $DocumentsUrl -TrustUrl $TrustUrl -BillingUrl $BillingUrl
$previewPriorChecks = $checks
$handler = [Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect = $false
$client = [Net.Http.HttpClient]::new($handler); $client.Timeout = [TimeSpan]::FromSeconds(45)
$http = [Net.Http.HttpClient]::new(); $http.Timeout = [TimeSpan]::FromSeconds(45)
$previewsApi = "$reviewApi/signing-previews"
function Create-Preview($Plan, [int]$Status = 201, $Headers = $bearer) {
    Api POST "$reviewApi/signing-preparations/$($Plan.id)/preview-pdfs" $null $Status $Headers
}
function Pdf-Bytes([string]$Url, $Headers = $bearer, [int]$Status = 200) {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, $Url)
    foreach ($name in $Headers.Keys) { $request.Headers.TryAddWithoutValidation($name, [string]$Headers[$name]) | Out-Null }
    try {
        $response = $http.SendAsync($request).GetAwaiter().GetResult()
        Assert-Check ([int]$response.StatusCode -eq $Status) "PDF expected $Status, got $([int]$response.StatusCode)"
        try {
            $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
            return @{ Bytes = $bytes; Headers = $response.Headers; ContentHeaders = $response.Content.Headers }
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}
function Hash-Bytes([byte[]]$Bytes) { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant() }
try {
    $snapshotPath = "$preparationApi/$($prepared.id)/preview-snapshot"
    Api GET $snapshotPath $null 401 @{} | Out-Null
    Api GET $snapshotPath $null 403 $guest.Headers | Out-Null
    $snapshot = Api GET $snapshotPath $null 200 $observer.Headers
    Assert-Check ($snapshot.id -eq $prepared.id -and $snapshot.stages.Count -eq 2 -and $snapshot.pdfSha256 -eq $pdf4.sha256) 'Snapshot pins the exact office preparation and original PDF'
    Api GET "$preparationApi/$([Guid]::NewGuid())/preview-snapshot" $null 404 | Out-Null
    Create-Preview $prepared 401 @{} | Out-Null
    Create-Preview $prepared 403 $guest.Headers | Out-Null
    Create-Preview $prepared 403 $translator.Headers | Out-Null
    Create-Preview $prepared 403 $observer.Headers | Out-Null
    Api POST "$reviewApi/signing-preparations/$([Guid]::NewGuid())/preview-pdfs" $null 404 | Out-Null
    $original = Pdf-Bytes "$reviewApi/pdfs/$($pdf4.id)"
    $originalCount = @(Api GET "$reviewApi/pdfs" $null 200).Count
    $preview = Create-Preview $prepared
    Assert-Check ($preview.kind -eq 'SigningPreviewUnsigned' -and $preview.sourcePdfSha256 -eq $pdf4.sha256 -and $preview.sha256 -ne $pdf4.sha256) 'Preview is a separate unsigned artifact with clearly separate source/preview hashes'
    Assert-Check ($preview.preparationId -eq $prepared.id -and $preview.createdByUserId -eq $ownerId -and $preview.sourcePdfVersionId -eq $pdf4.id) 'Preview records authenticated actor and authoritative IDs'
    Assert-Check (-not $preview.PSObject.Properties['storageKey'] -and -not $preview.PSObject.Properties['planSnapshotJson']) 'Private metadata must not expose storage paths or raw snapshots'
    $again = Create-Preview $prepared 200 $admin.Headers
    Assert-Check ($again.id -eq $preview.id -and $again.sha256 -eq $preview.sha256) 'Repeat generation must reuse identical archived bytes'
    $fileUrl = "$previewsApi/$($preview.id)/file"
    $file = Pdf-Bytes $fileUrl $translator.Headers
    Assert-Check ((Hash-Bytes $file.Bytes) -eq $preview.sha256 -and $file.Bytes.Length -eq $preview.sizeBytes) 'Preview bytes must match archived SHA and length'
    Assert-Check ([Text.Encoding]::ASCII.GetString($file.Bytes, 0, 5) -eq '%PDF-' -and $file.ContentHeaders.ContentType.MediaType -eq 'application/pdf') 'Preview download must be a PDF'
    Assert-Check ($file.Headers.CacheControl.NoStore -and $file.ContentHeaders.ContentDisposition.DispositionType -eq 'attachment' -and $file.Headers.GetValues('X-Artifact-Kind') -contains 'SigningPreviewUnsigned') 'Download must be private/no-store unsigned attachment'
    Assert-Check ($file.Headers.GetValues('X-Content-SHA256') -contains $preview.sha256 -and $file.Headers.GetValues('X-Source-PDF-SHA256') -contains $pdf4.sha256) 'Download labels both hashes separately'
    $range = Pdf-Bytes $fileUrl @{ Authorization = $bearer.Authorization; Range = 'bytes=0-9' } 206
    Assert-Check ($range.Bytes.Length -eq 10) 'Preview byte ranges must be supported'
    Pdf-Bytes $fileUrl @{} 401 | Out-Null
    Pdf-Bytes $fileUrl $guest.Headers 403 | Out-Null
    Pdf-Bytes "$previewsApi/$([Guid]::NewGuid())/file" $bearer 404 | Out-Null
    foreach ($member in @($bearer, $translator.Headers, $admin.Headers, $observer.Headers)) {
        $list = @(Api GET $previewsApi $null 200 $member)
        Assert-Check ($list.Count -eq 1 -and $list[0].id -eq $preview.id) 'All active members can read the preview history'
    }
    Api GET $previewsApi $null 403 $guest.Headers | Out-Null
    $wrongPreview = "$DocumentsUrl/api/v1/tenants/$($other.id)/translation-jobs/$jobId/documents/$($document.id)/signing-previews"
    Api GET $wrongPreview $null 404 | Out-Null
    Pdf-Bytes "$wrongPreview/$($preview.id)/file" $bearer 404 | Out-Null
    Api POST "$DocumentsUrl/api/v1/tenants/$tenantId/translation-jobs/$requiredJobId/documents/$($document.id)/signing-preparations/$($prepared.id)/preview-pdfs" $null 404 | Out-Null
    $after = Pdf-Bytes "$reviewApi/pdfs/$($pdf4.id)"
    Assert-Check ((Hash-Bytes $after.Bytes) -eq (Hash-Bytes $original.Bytes) -and @(Api GET "$reviewApi/pdfs" $null 200).Count -eq $originalCount) 'Preview must not replace or add draft PDF versions'
    Assert-Check ((Signing-State).preparations.Where({ $_.id -eq $prepared.id })[0].status -eq 'Prepared') 'Generation cannot record a signature or change Trust status'
    # Independent translation uses the same feature without an office stage or notary requirement.
    $independentPreview = Api POST "$independentApi/signing-preparations/$($soloPrepared.id)/preview-pdfs" $null 201
    Assert-Check ($independentPreview.sourcePdfVersionId -eq $independentPdf.id -and $soloPrepared.stages.Count -eq 1) 'Independent preview needs only its translator proposal'
    $soloFile = Pdf-Bytes "$independentApi/signing-previews/$($independentPreview.id)/file"
    Assert-Check ((Hash-Bytes $soloFile.Bytes) -eq $independentPreview.sha256) 'Independent preview download is verified'
    Api POST "$independentTrust/$($soloPrepared.id)/cancel" @{ expectedVersion = 0; reason = 'Development pagination test' } 200 | Out-Null
    $longSolo = $solo.Clone()
    $longSolo.translatorStatement = (("متن پیشنهادی صرفاً برای آزمایش صفحه‌بندی و حفظ تمام نویسه‌هاست؛ هیچ امضایی ثبت نشده است.`n" * 60).Substring(0, 4000))
    $longPlan = Api POST $independentTrust $longSolo 201
    # Parallel render requests may return busy, but must converge to a single archive.
    $raceUrl = "$independentApi/signing-preparations/$($longPlan.id)/preview-pdfs"
    $requests = 1..2 | ForEach-Object {
        $req = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, $raceUrl)
        $req.Headers.TryAddWithoutValidation('Authorization', $bearer.Authorization) | Out-Null
        @{ Request = $req; Task = $http.SendAsync($req) }
    }
    $statuses = @(); $raceIds = @()
    foreach ($pending in $requests) {
        $res = $pending.Task.GetAwaiter().GetResult()
        try {
            $statuses += [int]$res.StatusCode
            Assert-Check ([int]$res.StatusCode -in 200,201,429) 'Parallel preview requests can only create, reuse, or report busy'
            if ([int]$res.StatusCode -in 200,201) { $raceIds += (ConvertFrom-Json $res.Content.ReadAsStringAsync().GetAwaiter().GetResult()).id }
        } finally { $res.Dispose(); $pending.Request.Dispose() }
    }
    Assert-Check ($statuses -contains 201 -and @($raceIds | Select-Object -Unique).Count -eq 1) 'Parallel preview creation must have one winner and one immutable artifact'
    $longPreview = Api POST $raceUrl $null 200
    $longFile = Pdf-Bytes "$independentApi/signing-previews/$($longPreview.id)/file"
    Assert-Check ((Hash-Bytes $longFile.Bytes) -eq $longPreview.sha256 -and $longPlan.stages[0].proposedStatement.Length -eq 4000) 'Full-length proposal is archived without truncation'
    Assert-Check (@((Api GET "$independentApi/signing-previews" $null 200) | Where-Object preparationId -eq $longPlan.id).Count -eq 1) 'Database must enforce one preview per renderer and preparation'
    # Web BFF: owner can reuse; translators can download but cannot create; all mutations need CSRF.
    $loginPage = Web GET '/Account/Login' 200
    Web POST '/Account/Login' 302 (Fields @{ email = $email; password = $password; __RequestVerificationToken = (Csrf $loginPage.Body) }) | Out-Null
    $page = Web GET $signingPage 200
    Assert-Check ($page.Body -match '/Workspace/GenerateSigningPreview' -and $page.Body -match '/Workspace/SigningPreviewPdf') 'Manager sees generation and private download controls'
    $form = @{ tenantId = $tenantId; jobId = $jobId; preparationId = $prepared.id }
    Web POST '/Workspace/GenerateSigningPreview' 400 (Fields $form) | Out-Null
    $form.__RequestVerificationToken = Csrf $page.Body
    Web POST '/Workspace/GenerateSigningPreview' 302 (Fields $form) | Out-Null
    $webPdfPath = "/Workspace/SigningPreviewPdf?tenantId=$tenantId&jobId=$jobId&previewId=$($preview.id)"
    $webFile = $client.GetAsync("$WebUrl$webPdfPath").GetAwaiter().GetResult()
    try {
        Assert-Check ([int]$webFile.StatusCode -eq 200 -and (Hash-Bytes $webFile.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()) -eq $preview.sha256 -and $webFile.Headers.CacheControl.NoStore) 'BFF must verify and privately download the same bytes'
    } finally { $webFile.Dispose() }
    Web POST '/Account/Logout' 302 (Fields @{ __RequestVerificationToken = (Csrf $page.Body) }) | Out-Null
    Web GET $webPdfPath 302 | Out-Null
    $loginPage = Web GET '/Account/Login' 200
    Web POST '/Account/Login' 302 (Fields @{ email = $translator.Email; password = $translator.Password; __RequestVerificationToken = (Csrf $loginPage.Body) }) | Out-Null
    $page = Web GET $signingPage 200
    Assert-Check ($page.Body -notmatch '<form[^>]+action="/Workspace/GenerateSigningPreview' -and $page.Body -match '/Workspace/SigningPreviewPdf') 'Translator has download but not generation form'
    $form.__RequestVerificationToken = Csrf $page.Body
    Web POST '/Workspace/GenerateSigningPreview' 403 (Fields $form) | Out-Null
    Cancel-Preparation $prepared | Out-Null
    Create-Preview $prepared 409 | Out-Null
    Assert-Check ((Hash-Bytes (Pdf-Bytes $fileUrl).Bytes) -eq $preview.sha256) 'Cancellation preserves the historical unsigned preview'
    $fresh = Api POST $preparationApi (Proposal) 201
    Api POST "$reviewApi/reviews/$($review4.id)/decision" @{ action = 'Reopen'; note = 'Development preview stale-source check' } 200 $admin.Headers | Out-Null
    Create-Preview $fresh 409 | Out-Null
    Assert-Check ((Hash-Bytes (Pdf-Bytes $fileUrl).Bytes) -eq $preview.sha256) 'Reopening preserves historical preview downloads'
    Cancel-Preparation $fresh | Out-Null
    # Leave a readable multilingual fixture with a current approved PDF and an ungenerated proposal for browser QA.
    Api POST "$reviewApi/draft-revisions" @{ expectedCurrentRevision = 4; editorContentJson = '{"type":"doc","content":[{"type":"heading","attrs":{"level":2,"dir":"ltr"},"content":[{"type":"text","text":"Örnek çeviri — TEST ONLY"}]},{"type":"paragraph","attrs":{"dir":"rtl"},"content":[{"type":"text","text":"این یک ترجمهٔ آزمایشی است و سند رسمی نیست."}]},{"type":"paragraph","attrs":{"dir":"ltr"},"content":[{"type":"text","text":"Türkçe örnek: İstanbul, çeviri ve doğrulama. No legal document."}]}]}' } 201 $translator.Headers | Out-Null
    $sampleSource = Api POST "$reviewApi/draft-revisions/5/pdfs" $null 201 $translator.Headers
    $sampleReview = Api POST "$reviewApi/reviews" @{ expectedRevision = 5; expectedRound = 2; pdfVersionId = $sampleSource.id } 201 $translator.Headers
    Api POST "$reviewApi/reviews/$($sampleReview.id)/decision" @{ action = 'Approve' } 200 $admin.Headers | Out-Null
    $sampleProposal = Proposal
    $sampleProposal.translatorStatement = "این متن فقط بیانیهٔ پیشنهادی مترجم برای بررسی چیدمان است.`nهیچ امضایی ثبت نشده و این متن به‌معنای پذیرش مسئولیت نیست."
    $sampleProposal.officeStatement = "Bu metin yalnızca önerilen ofis beyanıdır.`nİmza veya noter onayı bulunmamaktadır."
    $samplePlan = Api POST $preparationApi $sampleProposal 201
    if ($previewOutputDirectory) {
        $target = [IO.Path]::GetFullPath($previewOutputDirectory)
        $null = New-Item -ItemType Directory -Path $target -Force
        [IO.File]::WriteAllBytes((Join-Path $target 'signing-preview-office-test.pdf'), $file.Bytes)
        [IO.File]::WriteAllBytes((Join-Path $target 'signing-preview-independent-test.pdf'), $soloFile.Bytes)
        [IO.File]::WriteAllBytes((Join-Path $target 'original-office-test.pdf'), $original.Bytes)
        [IO.File]::WriteAllBytes((Join-Path $target 'signing-preview-long-test.pdf'), $longFile.Bytes)
    }
    Write-Output "PASS: $($checks - $previewPriorChecks) signing-preview checks plus $previewPriorChecks baseline checks."
    if ($emitPreviewFixture) { Write-Output (ConvertTo-Json @{ email = $email; password = $password; tenantId = $tenantId; jobId = $jobId;
        documentId = $document.id; preparationId = $samplePlan.id; sourcePdfId = $sampleSource.id } -Compress) }
} finally { $http.Dispose(); $client.Dispose(); $handler.Dispose() }
