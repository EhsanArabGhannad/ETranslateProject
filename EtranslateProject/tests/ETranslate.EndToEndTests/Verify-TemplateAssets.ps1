#Requires -Version 7.0
param(
    [string]$IdentityUrl = 'http://localhost:5031',
    [string]$WorkflowUrl = 'http://localhost:5285',
    [string]$DocumentsUrl = 'http://localhost:5241'
)

$ErrorActionPreference = 'Stop'
$checks = 0

function Assert-Check($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}

function Request([string]$Method, [string]$Url, $Body, [int]$Status, $RequestHeaders = $headers) {
    $parameters = @{ Method = $Method; Uri = $Url; Headers = $RequestHeaders; SkipHttpErrorCheck = $true; TimeoutSec = 30 }
    if ($null -ne $Body) {
        $parameters.ContentType = 'application/json'
        $parameters.Body = ConvertTo-Json -InputObject $Body -Depth 20
    }
    $response = Invoke-WebRequest @parameters
    Assert-Check ($response.StatusCode -eq $Status) "$Method $Url expected $Status, got $($response.StatusCode): $($response.Content)"
    if ($response.Content -and $response.Headers['Content-Type'] -match 'json') {
        $responseText = if ($response.Content -is [byte[]]) {
            [Text.Encoding]::UTF8.GetString($response.Content)
        } else { [string]$response.Content }
        return ConvertFrom-Json -InputObject $responseText
    }
}

function Upload([byte[]]$Bytes, [string]$Type, [int]$Status,
    [string]$Url = "$templatesUrl/$($template.id)/assets") {
    $form = [System.Net.Http.MultipartFormDataContent]::new()
    try {
        $content = [System.Net.Http.ByteArrayContent]::new($Bytes)
        $content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new($Type)
        $form.Add($content, 'file', 'logo.png')
        $response = $client.PostAsync($Url, $form).GetAwaiter().GetResult()
        try {
            $json = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            Assert-Check ([int]$response.StatusCode -eq $Status) "Upload expected $Status, got $($response.StatusCode): $json"
            return ConvertFrom-Json -InputObject $json
        }
        finally { $response.Dispose() }
    }
    finally { $form.Dispose() }
}

# Every run creates a separate development tenant and leaves its fixtures for inspection.
$runId = [Guid]::NewGuid().ToString('N')
$credentials = @{ email = "assets-$runId@etranslate.local"; password = "Test-$runId!" }
$headers = @{}
Request 'POST' "$IdentityUrl/api/v1/auth/register" $credentials 200 | Out-Null
$login = Request 'POST' "$IdentityUrl/api/v1/auth/login" $credentials 200
$headers = @{ Authorization = "Bearer $($login.accessToken)" }
$tenant = Request 'POST' "$IdentityUrl/api/v1/tenants" @{ name = 'Asset smoke test'; slug = "assets-$runId"; type = 'TranslationOffice' } 201
$otherTenant = Request 'POST' "$IdentityUrl/api/v1/tenants" @{ name = 'Other asset tenant'; slug = "other-assets-$runId"; type = 'IndependentTranslator' } 201
$templatesUrl = "$DocumentsUrl/api/v1/tenants/$($tenant.id)/document-templates"
$templateBody = @{ name = 'Office A4'; editorContentJson = '{"type":"doc","content":[]}'; pageLayoutJson = '{"pageSize":"A4"}' }
$template = Request 'POST' $templatesUrl $templateBody 201
$secondBody = @{ name = 'Second office template'; editorContentJson = '{"type":"doc"}'; pageLayoutJson = '{}' }
$second = Request 'POST' $templatesUrl $secondBody 201
$client = [System.Net.Http.HttpClient]::new()
$client.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $login.accessToken)
try {
    [byte[]]$png = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jf1sAAAAASUVORK5CYII=')
    $asset = Upload $png 'image/png' 201
    [byte[]]$download = $client.GetByteArrayAsync("$templatesUrl/$($template.id)/assets/$($asset.id)").GetAwaiter().GetResult()
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($download)).ToLowerInvariant()
    Assert-Check ($hash -eq $asset.sha256) 'Downloaded asset hash mismatch'
    Upload ([Text.Encoding]::UTF8.GetBytes('not PNG')) 'image/png' 400 | Out-Null
    Upload ([Text.Encoding]::UTF8.GetBytes('<svg/>')) 'image/svg+xml' 400 | Out-Null
    Upload ([byte[]]::new(5242881)) 'image/png' 400 | Out-Null
    Request 'GET' "$templatesUrl/$($template.id)/assets/$($asset.id)" $null 401 @{} | Out-Null
    Request 'GET' "$templatesUrl/$($second.id)/assets/$($asset.id)" $null 404 | Out-Null
    Request 'GET' "$DocumentsUrl/api/v1/tenants/$($otherTenant.id)/document-templates/$($template.id)/assets/$($asset.id)" $null 404 | Out-Null

    $revisionBody = @{
        expectedCurrentRevision = 1
        editorContentJson = '{"type":"doc","content":[]}'
        headerContentJson = "{`"type`":`"image`",`"attrs`":{`"assetId`":`"$($asset.id)`"}}"
        pageLayoutJson = '{"pageSize":"A4"}'
    }
    Request 'POST' "$templatesUrl/$($second.id)/revisions" $revisionBody 400 | Out-Null
    $revision = Request 'POST' "$templatesUrl/$($template.id)/revisions" $revisionBody 201
    $job = Request 'POST' "$WorkflowUrl/api/v1/tenants/$($tenant.id)/translation-jobs" @{
        title = 'Pinned template verification'; sourceLanguageCode = 'fa'; targetLanguageCode = 'tr'; notaryRequirement = 'NotRequired'
    } 201
    $documentUrl = "$DocumentsUrl/api/v1/tenants/$($tenant.id)/translation-jobs/$($job.id)/documents"
    $document = Request 'POST' $documentUrl $null 201
    $baseUrl = "$documentUrl/$($document.id)"
    Upload $png 'image/png' 201 "$baseUrl/source-files" | Out-Null
    Request 'POST' "$baseUrl/template" @{ templateId = $template.id; revisionNumber = 999 } 404 | Out-Null
    $applied = Request 'POST' "$baseUrl/template" @{ templateId = $template.id; revisionNumber = 2 } 200
    Assert-Check ($applied.templateRevisionId -eq $revision.id -and $applied.currentDraftRevision -eq 1) 'Wrong pinned revision or initial draft number'
    Assert-Check ($applied.sourceFiles.Count -eq 1) 'Template application lost existing source file metadata'
    Request 'POST' "$baseUrl/template" @{ templateId = $template.id; revisionNumber = 2 } 409 | Out-Null
    $revisionBody.expectedCurrentRevision = 2
    $revisionBody.headerContentJson = '{"text":"New layout"}'
    Request 'POST' "$templatesUrl/$($template.id)/revisions" $revisionBody 201 | Out-Null
    Request 'PUT' "$templatesUrl/$($template.id)/status" @{ isActive = $false } 200 | Out-Null
    $pinned = Request 'GET' "$baseUrl/template" $null 200
    Assert-Check ($pinned.id -eq $revision.id -and $pinned.headerContentJson.Contains($asset.id)) 'Pinned layout changed after template update/archive'
    Request 'GET' "$templatesUrl/$($template.id)/assets/$($asset.id)" $null 200 | Out-Null

    $invalidDraft = @{ expectedCurrentRevision = 1; editorContentJson = "{`"assetId`":`"$([Guid]::NewGuid())`"}" }
    Request 'POST' "$baseUrl/draft-revisions" $invalidDraft 400 | Out-Null
    # Both saves start with the same revision. Exactly one may commit.
    $draftBody = ConvertTo-Json @{ expectedCurrentRevision = 1; editorContentJson = "{`"assetId`":`"$($asset.id)`"}" }
    $firstContent = [System.Net.Http.StringContent]::new($draftBody, [Text.Encoding]::UTF8, 'application/json')
    $secondContent = [System.Net.Http.StringContent]::new($draftBody, [Text.Encoding]::UTF8, 'application/json')
    try {
        $firstTask = $client.PostAsync("$baseUrl/draft-revisions", $firstContent)
        $secondTask = $client.PostAsync("$baseUrl/draft-revisions", $secondContent)
        $firstResponse = $firstTask.GetAwaiter().GetResult()
        $secondResponse = $secondTask.GetAwaiter().GetResult()
        try {
            $statuses = @([int]$firstResponse.StatusCode, [int]$secondResponse.StatusCode) | Sort-Object
            Assert-Check ($statuses[0] -eq 201 -and $statuses[1] -eq 409) "Concurrent saves returned $statuses"
        }
        finally { $firstResponse.Dispose(); $secondResponse.Dispose() }
    }
    finally { $firstContent.Dispose(); $secondContent.Dispose() }
    $after = Request 'GET' $documentUrl $null 200
    Assert-Check ($after.templateRevisionId -eq $revision.id -and $after.currentDraftRevision -eq 2) 'Draft save changed template pin or revision number'
    [pscustomobject]@{ ChecksPassed = $checks; TenantId = $tenant.id; DocumentId = $document.id; AssetSha256 = $hash }
}
finally { $client.Dispose() }
