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
function Generate-Pdf([int]$Revision, [int]$Status = 200, [bool]$WithCsrf = $true) {
    $values = @{ tenantId = $tenantId; jobId = $pdfJobId; revisionNumber = $Revision }
    if ($WithCsrf) { $values.__RequestVerificationToken = $pdfCsrf }
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, "$WebUrl/Workspace/GenerateDraftPdf")
    $request.Headers.Accept.Add([Net.Http.Headers.MediaTypeWithQualityHeaderValue]::new('application/json'))
    $request.Content = Fields $values
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            Assert-Check ([int]$response.StatusCode -eq $Status) "PDF generation expected $Status, got $([int]$response.StatusCode): $body"
            if ($response.Content.Headers.ContentType.MediaType -match 'json') { return ConvertFrom-Json $body }
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}
function Download-Pdf([string]$Path, [int]$Status = 200) {
    $response = $client.GetAsync("$WebUrl$Path").GetAwaiter().GetResult()
    try {
        Assert-Check ([int]$response.StatusCode -eq $Status) "PDF download expected $Status, got $([int]$response.StatusCode)"
        if ($Status -ne 200) { return }
        $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        Assert-Check ($response.Content.Headers.ContentType.MediaType -eq 'application/pdf') 'PDF type missing'
        Assert-Check ($response.Content.Headers.ContentDisposition.DispositionType -eq 'attachment') 'PDF must be an attachment'
        Assert-Check (($response.Headers.GetValues('Cache-Control') -join '') -match 'no-store') 'PDF must not be cached'
        Assert-Check ([Text.Encoding]::ASCII.GetString($bytes[0..4]) -eq '%PDF-') 'PDF signature missing'
        return ,$bytes
    } finally { $response.Dispose() }
}
try {
    $loginPage = Web GET '/Account/Login' 200
    Web POST '/Account/Login' 302 (Fields @{ email = $email; password = $password; __RequestVerificationToken = (Csrf $loginPage.Body) }) | Out-Null
    $workspace = Web GET "/Workspace?tenantId=$tenantId" 200; $csrf = Csrf $workspace.Body
    Assert-Check ($workspace.Body -match 'name="pageSize"' -and $workspace.Body -match 'name="marginTop"') 'Page layout controls missing'
    Web POST '/Workspace/CreateTemplate' 302 (Fields @{ tenantId = $tenantId; name = 'Rejected layout'; pageSize = 'A3'; __RequestVerificationToken = $csrf }) | Out-Null
    Assert-Check (@(Api GET $templatesPath $null 200).Count -eq 1) 'Invalid Web layout must not create a template'
    Web POST '/Workspace/CreateTemplate' 302 (Fields @{
        tenantId = $tenantId; name = 'PDF sample layout'; pageSize = 'A4'; orientation = 'Portrait'; marginTop = 15; marginRight = 17; marginBottom = 15; marginLeft = 17
        header = "دارالترجمه‌ی نمونه - صرفاً آزمایشی`nÖrnek Tercüme Bürosu - TEST"; footer = "سند آزمایشی؛ بدون امضا یا تأیید رسمی`nYalnızca test amaçlıdır."; starterText = ''; watermark = 'SAMPLE'
        __RequestVerificationToken = $csrf
    }) | Out-Null
    $pdfTemplate = @(Api GET $templatesPath $null 200) | Where-Object name -eq 'PDF sample layout'
    $pdfTemplateDetail = Api GET "$templatesPath/$($pdfTemplate.id)" $null 200
    $layout = ConvertFrom-Json $pdfTemplateDetail.revision.pageLayoutJson
    Assert-Check ($layout.pageSize -eq 'A4' -and $layout.marginsMm.left -eq 17) 'Selected layout must persist'
    $form = [Net.Http.MultipartFormDataContent]::new()
    $form.Add([Net.Http.StringContent]::new($csrf), '__RequestVerificationToken'); $form.Add([Net.Http.StringContent]::new($tenantId), 'tenantId')
    $form.Add([Net.Http.StringContent]::new([string]$pdfTemplate.id), 'templateId')
    $image = [Net.Http.ByteArrayContent]::new($png); $image.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('image/png')
    $form.Add($image, 'file', 'sample-logo.png'); Web POST '/Workspace/AddLogo' 302 $form | Out-Null
    $pdfTemplateDetail = Api GET "$templatesPath/$($pdfTemplate.id)" $null 200
    $pdfJob = Api POST "$WorkflowUrl/api/v1/tenants/$tenantId/translation-jobs" @{
        title = 'Persian Turkish PDF sample'; sourceLanguageCode = 'fa'; targetLanguageCode = 'tr'; notaryRequirement = 'NotRequired'
    } 201
    $pdfJobId = $pdfJob.id
    $pdfDocumentPath = "$DocumentsUrl/api/v1/tenants/$tenantId/translation-jobs/$pdfJobId/documents"
    $pdfDocument = Api POST $pdfDocumentPath $null 201
    $pdfApi = "$pdfDocumentPath/$($pdfDocument.id)"
    $pdfEditorPath = "/Workspace/Editor?tenantId=$tenantId&jobId=$pdfJobId"
    $emptyEditor = Web GET $pdfEditorPath 200
    Assert-Check ($emptyEditor.Body -notmatch 'id="draft-pdf-form"') 'Unsaved document must not offer PDF generation'
    Api POST "$pdfApi/template" @{ templateId = $pdfTemplate.id; revisionNumber = 2 } 200 | Out-Null
    $nodes = [Collections.Generic.List[object]]::new()
    $nodes.Add(@{ type = 'heading'; attrs = @{ level = 2; dir = 'rtl' }; content = @(@{ type = 'text'; text = 'نمونه‌ی ترجمه‌ی فارسی و ترکی'; marks = @(@{ type = 'bold' }) }) })
    $nodes.Add(@{ type = 'paragraph'; attrs = @{ dir = 'ltr' }; content = @(@{ type = 'text'; text = 'Türkçe çeviri - İ ı Ş ş Ç ç Ö ö Ü ü Ğ ğ'; marks = @(@{ type = 'italic' }, @{ type = 'underline' }) }) })
    for ($paragraph = 1; $paragraph -le 24; $paragraph++) {
        $nodes.Add(@{ type = 'paragraph'; attrs = @{ dir = 'rtl' }; content = @(
            @{ type = 'text'; text = "بند ${paragraph}: این متن صرفاً برای بررسی پیوستگی حروف فارسی، فاصله‌ها و صفحه‌بندی خودکار است. هیچ اطلاعات شخصی یا اعتبار حقوقی ندارد." },
            @{ type = 'hardBreak' }, @{ type = 'text'; text = "Madde ${paragraph}: Türkçe karakterlerin ve sayfa düzeninin doğru görüntülenmesi için örnek metin." }
        ) })
    }
    $nodes.Add(@{ type = 'orderedList'; attrs = @{ dir = 'ltr'; start = 3; type = 'a' }; content = @(
        @{ type = 'listItem'; content = @(@{ type = 'paragraph'; content = @(@{ type = 'text'; text = 'First numbered item' }) }) },
        @{ type = 'listItem'; content = @(@{ type = 'paragraph'; content = @(@{ type = 'text'; text = 'Second numbered item' }) }) }
    ) })
    $richJson = ConvertTo-Json -InputObject @{ type = 'doc'; attrs = @{ dir = 'auto' }; content = $nodes.ToArray() } -Depth 20 -Compress
    Api POST "$pdfApi/draft-revisions" @{ expectedCurrentRevision = 1; editorContentJson = $richJson; plainText = $null } 201 | Out-Null
    $editor = Web GET $pdfEditorPath 200; $pdfCsrf = Csrf $editor.Body
    Generate-Pdf 2 400 $false | Out-Null
    Generate-Pdf 99 404 | Out-Null
    $firstPdf = (Generate-Pdf 2).pdf
    Assert-Check ($firstPdf.kind -eq 'DraftUnsigned' -and $firstPdf.revisionNumber -eq 2) 'PDF must explicitly be an unsigned saved draft'
    Assert-Check ($firstPdf.templateRevisionId -eq $pdfTemplateDetail.revision.id) 'PDF must retain exact pinned template'
    Assert-Check ($firstPdf.sha256 -match '^[a-f0-9]{64}$' -and $firstPdf.sizeBytes -gt 0) 'PDF digest and size required'
    $pdfPath = "/Workspace/DraftPdf?tenantId=$tenantId&jobId=$pdfJobId&pdfId=$($firstPdf.id)"
    [byte[]]$bytes = Download-Pdf $pdfPath
    Assert-Check ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() -eq $firstPdf.sha256) 'Downloaded PDF must match archived digest'
    $outputDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../output/pdf')); [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $outputDirectory 'translation-draft-sample.pdf'), $bytes)
    $cachedPdf = (Generate-Pdf 2).pdf
    Assert-Check ($cachedPdf.id -eq $firstPdf.id -and $cachedPdf.sha256 -eq $firstPdf.sha256 -and $cachedPdf.createdAtUtc -eq $firstPdf.createdAtUtc) 'Repeated generation must return the same archived bytes and identity'
    Assert-Check ((Api GET $pdfDocumentPath $null 200).currentDraftRevision -eq 2) 'Generating PDF must not change translation revision'
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, "$WebUrl$pdfPath"); $request.Headers.Range = [Net.Http.Headers.RangeHeaderValue]::new(0, 7)
    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    try {
        Assert-Check ([int]$response.StatusCode -eq 206 -and $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult().Length -eq 8) 'PDF byte ranges must return the exact range'
        Assert-Check (($response.Headers.GetValues('X-Content-SHA256') -join '') -eq $firstPdf.sha256) 'PDF response must expose the archived hash'
    } finally { $response.Dispose(); $request.Dispose() }
    Download-Pdf "/Workspace/DraftPdf?tenantId=$($other.id)&jobId=$pdfJobId&pdfId=$($firstPdf.id)" 404
    Download-Pdf "/Workspace/DraftPdf?tenantId=$([Guid]::NewGuid())&jobId=$pdfJobId&pdfId=$($firstPdf.id)" 403
    Download-Pdf "/Workspace/DraftPdf?tenantId=$tenantId&jobId=$jobId&pdfId=$($firstPdf.id)" 404
    Api GET "$pdfApi/pdfs/$($firstPdf.id)" $null 401 @{} | Out-Null
    Api POST "$templatesPath/$($pdfTemplate.id)/revisions" @{
        expectedCurrentRevision = 2; editorContentJson = $pdfTemplateDetail.revision.editorContentJson; headerContentJson = '{"type":"doc","content":[]}'
        footerContentJson = $pdfTemplateDetail.revision.footerContentJson; pageLayoutJson = $pdfTemplateDetail.revision.pageLayoutJson; watermarkJson = $pdfTemplateDetail.revision.watermarkJson
    } 201 | Out-Null
    Api POST "$pdfApi/draft-revisions" @{ expectedCurrentRevision = 2; editorContentJson = $richJson.Replace('نمونه‌ی ترجمه‌ی فارسی و ترکی', 'نسخه‌ی جدید آزمایشی'); plainText = $null } 201 | Out-Null
    $newPdf = (Generate-Pdf 3).pdf
    Assert-Check ($newPdf.id -ne $firstPdf.id -and $newPdf.sha256 -ne $firstPdf.sha256 -and $newPdf.templateRevisionId -eq $firstPdf.templateRevisionId) 'New draft PDF must use a new identity while retaining pinned layout'
    [byte[]]$oldBytes = Download-Pdf $pdfPath
    Assert-Check ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($oldBytes)).ToLowerInvariant() -eq $firstPdf.sha256) 'New drafts/templates must never change an archived PDF'
    $historical = Web GET "$pdfEditorPath&revisionNumber=2" 200
    Assert-Check ($historical.Body -match 'name="revisionNumber" value="2"' -and $historical.Body -match [regex]::Escape($firstPdf.sha256)) 'Historical view must export its displayed revision and retain prior PDFs'
    $unknown = '{"type":"doc","content":[{"type":"table","custom":"must-not-be-dropped","content":[]}]}'
    Api POST "$pdfApi/draft-revisions" @{ expectedCurrentRevision = 3; editorContentJson = $unknown; plainText = $null } 201 | Out-Null
    $rejected = Generate-Pdf 4 422
    Assert-Check ($rejected.message -match 'PDF') 'Unsupported content must report a usable error'
    Assert-Check (@(Api GET "$pdfApi/pdfs" $null 200).Count -eq 2) 'Failed rendering must not create PDF metadata'
    Assert-Check ((Api GET "$pdfApi/draft-revisions/4" $null 200).editorContentJson -eq $unknown) 'Failed rendering must preserve original unknown content'
    $page = Web GET $pdfEditorPath 200
    Web POST '/Account/Logout' 302 (Fields @{ __RequestVerificationToken = (Csrf $page.Body) }) | Out-Null
    Download-Pdf $pdfPath 302
    Write-Output "PASS: $($checks - $baselineChecks) PDF checks, plus $baselineChecks baseline checks. Sample PDF: output/pdf/translation-draft-sample.pdf"
    if ($EmitBrowserFixture) { [pscustomobject]@{ email = $email; password = $password; editorPath = "$pdfEditorPath&revisionNumber=3"; pdfId = $newPdf.id; tenantId = $tenantId; jobId = $pdfJobId } | ConvertTo-Json }
} finally { $client.Dispose(); $handler.Dispose() }
