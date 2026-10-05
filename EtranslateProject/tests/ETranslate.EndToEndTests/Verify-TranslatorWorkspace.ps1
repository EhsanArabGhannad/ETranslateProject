#Requires -Version 7.0
param(
    [string]$WebUrl = 'https://localhost:7049',
    [string]$IdentityUrl = 'http://localhost:5031',
    [string]$WorkflowUrl = 'http://localhost:5285',
    [string]$DocumentsUrl = 'http://localhost:5241',
    [switch]$EmitBrowserFixture
)
$ErrorActionPreference = 'Stop'
# Reuse the existing isolated onboarding/cookie/CSRF fixtures and helper functions.
. "$PSScriptRoot/Verify-WebWorkspace.ps1" -WebUrl $WebUrl -IdentityUrl $IdentityUrl -WorkflowUrl $WorkflowUrl -DocumentsUrl $DocumentsUrl
$baseChecks = $checks
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(45)
function Upload-Source([byte[]]$Bytes, [string]$Type, [string]$Name, [int]$Status, [bool]$WithCsrf = $true) {
    $form = [Net.Http.MultipartFormDataContent]::new()
    $form.Add([Net.Http.StringContent]::new($tenantId), 'tenantId')
    $form.Add([Net.Http.StringContent]::new($jobId), 'jobId')
    if ($WithCsrf) { $form.Add([Net.Http.StringContent]::new($csrf), '__RequestVerificationToken') }
    $content = [Net.Http.ByteArrayContent]::new($Bytes)
    $content.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new($Type)
    $form.Add($content, 'file', $Name)
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, "$WebUrl/Workspace/UploadSource")
    $request.Headers.Accept.Add([Net.Http.Headers.MediaTypeWithQualityHeaderValue]::new('application/json'))
    $request.Content = $form
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            Assert-Check ([int]$response.StatusCode -eq $Status) "Source upload expected $Status, got $([int]$response.StatusCode)"
            if ($response.Content.Headers.ContentType.MediaType -eq 'application/json') {
                return ConvertFrom-Json ($response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
            }
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}
try {
    $loginPage = Web GET '/Account/Login' 200
    Web POST '/Account/Login' 302 (Fields @{ email = $email; password = $password; __RequestVerificationToken = (Csrf $loginPage.Body) }) | Out-Null
    $page = Web GET $editorPath 200
    $csrf = Csrf $page.Body
    $source = Upload-Source $png 'image/png' 'source.png' 200
    $sourceId = $source.sourceFile.id
    $sourcePath = "/Workspace/Source?tenantId=$tenantId&jobId=$jobId&sourceFileId=$sourceId"
    $response = $client.GetAsync("$WebUrl$sourcePath&download=true").GetAwaiter().GetResult()
    try {
        $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        Assert-Check ([int]$response.StatusCode -eq 200) 'Source download failed'
        Assert-Check ($response.Content.Headers.ContentDisposition.DispositionType -eq 'attachment') 'Download must use attachment disposition'
        Assert-Check ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() -eq $source.sourceFile.sha256) 'Original source digest must match'
        Assert-Check ($response.Headers.GetValues('Cache-Control') -join '' -match 'no-store') 'Private source response must not be cached'
    } finally { $response.Dispose() }
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, "$WebUrl$sourcePath")
    $request.Headers.Range = [Net.Http.Headers.RangeHeaderValue]::new(0, 7)
    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    try {
        Assert-Check ([int]$response.StatusCode -eq 206) 'Source range request must return 206'
        Assert-Check ($response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult().Length -eq 8) 'Source range must have exact requested length'
    } finally { $response.Dispose(); $request.Dispose() }
    $samplePath = Join-Path $PSScriptRoot '../../output/pdf/workspace-source-sample.pdf'
    if (-not (Test-Path -LiteralPath $samplePath)) { throw 'Generate the sample fixture using Fixtures/CreateWorkspaceSourceSample.py first.' }
    $pdfSource = Upload-Source ([IO.File]::ReadAllBytes($samplePath)) 'application/pdf' 'two-pages.pdf' 200
    Upload-Source $png 'image/png' 'missing-csrf.png' 400 $false | Out-Null
    Upload-Source ([Text.Encoding]::UTF8.GetBytes('not a PNG')) 'image/png' 'fake.png' 400 | Out-Null
    Upload-Source ([Text.Encoding]::UTF8.GetBytes('<svg/>')) 'image/svg+xml' 'bad.svg' 400 | Out-Null
    Upload-Source ([byte[]]::new(26214401)) 'application/pdf' 'oversized.pdf' 400 | Out-Null
    Web GET "/Workspace/Source?tenantId=$($other.id)&jobId=$jobId&sourceFileId=$sourceId" 404 | Out-Null
    Web GET "/Workspace/Source?tenantId=$tenantId&jobId=$jobId&sourceFileId=$([Guid]::NewGuid())" 404 | Out-Null
    Web GET "/Workspace/Source?tenantId=$([Guid]::NewGuid())&jobId=$jobId&sourceFileId=$sourceId" 403 | Out-Null
    $beforeRich = Api GET $documentPath $null 200
    Assert-Check ($beforeRich.currentDraftRevision -eq 2 -and $beforeRich.sourceFiles.Count -eq 2) 'Uploading sources must not create translation revisions'

    $rich = @{ type = 'doc'; attrs = @{ dir = 'auto' }; content = @(
        @{ type = 'heading'; attrs = @{ level = 2; dir = 'rtl' }; content = @(@{ type = 'text'; text = 'ترجمه‌ی رسمی آزمایشی'; marks = @(@{ type = 'bold' }) }) },
        @{ type = 'paragraph'; attrs = @{ dir = 'ltr' }; content = @(@{ type = 'text'; text = 'Türkçe çeviri'; marks = @(@{ type = 'italic' }, @{ type = 'underline' }) }) },
        @{ type = 'bulletList'; attrs = @{ dir = 'rtl' }; content = @(@{ type = 'listItem'; attrs = @{ dir = 'rtl' }; content = @(@{ type = 'paragraph'; attrs = @{ dir = 'rtl' }; content = @(@{ type = 'text'; text = 'بند نمونه' }) }) }) }
    ) }
    $richJson = ConvertTo-Json -InputObject $rich -Depth 20 -Compress
    $save = @{ tenantId = $tenantId; jobId = $jobId; expectedRevision = 2; mode = 'rich'; editorContentJson = $richJson; __RequestVerificationToken = $csrf }
    Web POST '/Workspace/SaveDraft' 302 (Fields $save) | Out-Null
    $current = Api GET $documentPath $null 200
    Assert-Check ($current.currentDraftRevision -eq 3 -and $current.templateRevisionId -eq $beforeRich.templateRevisionId) 'Rich save must preserve pinned template'
    $draft = Api GET "$documentPath/$($current.id)/draft-revisions/3" $null 200
    Assert-Check ($draft.editorContentJson -eq $richJson) 'Rich JSON must be stored without flattening or reformatting'
    Assert-Check ($draft.plainText -match 'Türkçe çeviri') 'Rich draft plain-text metadata missing'
    $page = Web GET $editorPath 200
    Assert-Check ($page.Body -match 'data-rich-compatible="true"') 'Supported rich document must be eligible for the editor'
    Assert-Check ($page.Body -match 'two-pages.pdf' -and $page.Body -match 'source.png') 'Source metadata must remain visible after rich save'
    $save.editorContentJson = $richJson.Replace('Türkçe çeviri', 'ConflictCandidate')
    $stale = Web POST '/Workspace/SaveDraft' 409 (Fields $save)
    Assert-Check ($stale.Body -match 'ConflictCandidate' -and $stale.Body -match 'data-editable="false"') 'Rich conflict must preserve input and prevent accidental retry'
    Web POST '/Workspace/SaveDraft' 400 (Fields @{ tenantId = $tenantId; jobId = $jobId; expectedRevision = 3; mode = 'text'; plainText = 'Do not flatten'; __RequestVerificationToken = $csrf }) | Out-Null
    Web POST '/Workspace/SaveDraft' 400 (Fields @{ tenantId = $tenantId; jobId = $jobId; expectedRevision = 3; mode = 'rich'; editorContentJson = '{"type":"doc","content":[{"type":"image","attrs":{"src":"https://example.org/track.png"}}]}'; __RequestVerificationToken = $csrf }) | Out-Null
    $afterRejected = Api GET $documentPath $null 200
    Assert-Check ($afterRejected.currentDraftRevision -eq 3) 'Invalid/flattening writes must not change revision counter'
    $unknownJson = '{"type":"doc","content":[{"type":"table","customData":"keep-me","content":[]}]}'
    Api POST "$documentPath/$($current.id)/draft-revisions" @{ expectedCurrentRevision = 3; editorContentJson = $unknownJson; plainText = $null } 201 | Out-Null
    $fallback = Web GET $editorPath 200
    Assert-Check ($fallback.Body -match 'data-rich-compatible="false"' -and $fallback.Body -match 'keep-me') 'Unsupported old content must remain intact in advanced fallback'
    $save.expectedRevision = 4; $save.editorContentJson = $richJson
    Web POST '/Workspace/SaveDraft' 302 (Fields $save) | Out-Null
    $historical = Web GET "$editorPath&revisionNumber=3" 200
    Assert-Check ($historical.Body -match 'data-editable="false"') 'Historical rich editor must be read-only'
    $page = Web GET $editorPath 200
    Web POST '/Account/Logout' 302 (Fields @{ __RequestVerificationToken = (Csrf $page.Body) }) | Out-Null
    Web GET $sourcePath 302 | Out-Null
    Write-Output "PASS: $($checks - $baseChecks) translator-workspace checks, plus $baseChecks baseline checks."
    if ($EmitBrowserFixture) { [pscustomobject]@{ email = $email; password = $password; editorPath = $editorPath; pdfId = $pdfSource.sourceFile.id } | ConvertTo-Json }
} finally { $client.Dispose(); $handler.Dispose() }
