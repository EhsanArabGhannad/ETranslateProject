#Requires -Version 7.0
param(
    [string]$WebUrl = 'https://localhost:7049',
    [string]$IdentityUrl = 'http://localhost:5031',
    [string]$WorkflowUrl = 'http://localhost:5285',
    [string]$DocumentsUrl = 'http://localhost:5241'
)
$ErrorActionPreference = 'Stop'
$script:checks = 0
function Assert-Check($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}
function Fields($Values) {
    $data = [Collections.Generic.Dictionary[string,string]]::new()
    foreach ($key in $Values.Keys) { $data.Add($key, [string]$Values[$key]) }
    return [Net.Http.FormUrlEncodedContent]::new($data)
}
function Web([string]$Method, [string]$Path, [int]$Status, $Content = $null) {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method), "$WebUrl$Path")
    if ($Content) { $request.Content = $Content }
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            Assert-Check ([int]$response.StatusCode -eq $Status) "$Method $Path expected $Status, got $([int]$response.StatusCode): $body"
            return @{ Body = $body; Location = [string]$response.Headers.Location; Headers = $response.Headers }
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}
function Csrf([string]$Html) {
    $match = [regex]::Match($Html, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')
    Assert-Check $match.Success 'Antiforgery field missing'
    return [Net.WebUtility]::HtmlDecode($match.Groups[1].Value)
}
function Api([string]$Method, [string]$Url, $Body, [int]$Status, $Headers = $bearer) {
    $parameters = @{ Uri = $Url; Method = $Method; Headers = $Headers; SkipHttpErrorCheck = $true; TimeoutSec = 30 }
    if ($null -ne $Body) { $parameters.Body = ConvertTo-Json -InputObject $Body -Depth 15; $parameters.ContentType = 'application/json' }
    $response = Invoke-WebRequest @parameters
    Assert-Check ($response.StatusCode -eq $Status) "API $Method $Url expected $Status, got $($response.StatusCode)"
    if ($response.Content) {
        $json = if ($response.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($response.Content) } else { [string]$response.Content }
        return ConvertFrom-Json -InputObject $json
    }
}

# Each run leaves isolated development fixtures for manual inspection; no production cleanup.
$runId = [Guid]::NewGuid().ToString('N')
$email = "web-$runId@etranslate.local"
$password = "Web-$runId!"
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(40)
try {
    $anonymous = Web GET '/Workspace' 302
    Assert-Check ($anonymous.Location -match '/Account/Login') 'Anonymous workspace must require login'
    Web POST '/Account/Register' 400 (Fields @{ email = $email; password = $password }) | Out-Null
    $registration = Web GET '/Account/Register' 200
    $signedIn = Web POST '/Account/Register' 302 (Fields @{ email = $email; password = $password; __RequestVerificationToken = (Csrf $registration.Body) })
    Assert-Check ($signedIn.Headers.GetValues('Set-Cookie') -join ';' -match 'httponly') 'Authentication cookie must be HttpOnly'
    $page = Web GET '/Workspace' 200
    Assert-Check ($page.Body -notmatch 'access_token|localStorage') 'Tokens must not appear in rendered HTML'
    $tenantRedirect = Web POST '/Workspace/CreateTenant' 302 (Fields @{ name = 'Web smoke office'; slug = "web-$runId"; type = 'TranslationOffice'; __RequestVerificationToken = (Csrf $page.Body) })
    $tenantId = [regex]::Match($tenantRedirect.Location, 'tenantId=([a-f0-9-]+)').Groups[1].Value
    Assert-Check (-not [string]::IsNullOrEmpty($tenantId)) 'Tenant creation must select new workspace'
    $login = Api POST "$IdentityUrl/api/v1/auth/login" @{ email = $email; password = $password } 200 @{}
    $bearer = @{ Authorization = "Bearer $($login.accessToken)" }
    $page = Web GET "/Workspace?tenantId=$tenantId" 200
    $csrf = Csrf $page.Body
    Web POST '/Workspace/CreateTemplate' 302 (Fields @{ tenantId = $tenantId; name = 'Web A4'; header = 'Office letterhead'; footer = 'Office footer'; starterText = 'Initial translation'; watermark = 'DRAFT'; __RequestVerificationToken = $csrf }) | Out-Null
    $templatesPath = "$DocumentsUrl/api/v1/tenants/$tenantId/document-templates"
    $templates = @(Api GET $templatesPath $null 200)
    Assert-Check ($templates.Count -eq 1) 'Expected one web-created template'
    $templateId = $templates[0].id
    $form = [Net.Http.MultipartFormDataContent]::new()
    $form.Add([Net.Http.StringContent]::new($csrf), '__RequestVerificationToken')
    $form.Add([Net.Http.StringContent]::new($tenantId), 'tenantId')
    $form.Add([Net.Http.StringContent]::new([string]$templateId), 'templateId')
    [byte[]]$png = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jf1sAAAAASUVORK5CYII=')
    $image = [Net.Http.ByteArrayContent]::new($png)
    $image.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('image/png')
    $form.Add($image, 'file', 'smoke.png')
    Web POST '/Workspace/AddLogo' 302 $form | Out-Null
    $detail = Api GET "$templatesPath/$templateId" $null 200
    Assert-Check ($detail.currentRevision -eq 2) 'Logo attachment must create immutable template revision 2'
    $assetId = (ConvertFrom-Json $detail.revision.headerContentJson).content[-1].attrs.assetId
    $assetPath = "/Workspace/Asset?tenantId=$tenantId&templateId=$templateId&assetId=$assetId"
    Web GET $assetPath 200 | Out-Null
    $jobRedirect = Web POST '/Workspace/CreateJob' 302 (Fields @{ tenantId = $tenantId; title = 'Web translation'; sourceLanguageCode = 'fa'; targetLanguageCode = 'tr'; notaryRequirement = 'NotRequired'; acceptanceProfile = ''; __RequestVerificationToken = $csrf })
    $editorPath = $jobRedirect.Location
    $jobId = [regex]::Match($editorPath, 'jobId=([a-f0-9-]+)').Groups[1].Value
    Assert-Check (-not [string]::IsNullOrEmpty($jobId)) 'Created job must open editor'
    $job = Api GET "$WorkflowUrl/api/v1/tenants/$tenantId/translation-jobs/$jobId" $null 200
    Assert-Check ($job.notaryRequirement -eq 'NotRequired' -and $null -eq $job.acceptanceProfile) 'Notary/profile defaults must remain optional'
    $requiredJobRedirect = Web POST '/Workspace/CreateJob' 302 (Fields @{ tenantId = $tenantId; title = 'Notary and other profile'; sourceLanguageCode = 'en'; targetLanguageCode = 'tr'; notaryRequirement = 'Required'; notaryProcessingMode = 'Hybrid'; acceptanceProfile = 'Other'; acceptanceProfileOther = 'Custom destination'; __RequestVerificationToken = $csrf })
    $requiredJobId = [regex]::Match($requiredJobRedirect.Location, 'jobId=([a-f0-9-]+)').Groups[1].Value
    $requiredJob = Api GET "$WorkflowUrl/api/v1/tenants/$tenantId/translation-jobs/$requiredJobId" $null 200
    Assert-Check ($requiredJob.notaryRequirement -eq 'Required' -and $requiredJob.notaryProcessingMode -eq 'Hybrid' -and $requiredJob.acceptanceProfile -eq 'Other' -and $requiredJob.acceptanceProfileOther -eq 'Custom destination') 'Required notary and Other profile must retain values'
    $page = Web GET $editorPath 200
    Web POST '/Workspace/StartDocument' 302 (Fields @{ tenantId = $tenantId; jobId = $jobId; __RequestVerificationToken = (Csrf $page.Body) }) | Out-Null
    $page = Web GET $editorPath 200
    $csrf = Csrf $page.Body
    Web POST '/Workspace/ApplyTemplate' 302 (Fields @{ tenantId = $tenantId; jobId = $jobId; templateId = $templateId; revisionNumber = 2; __RequestVerificationToken = $csrf }) | Out-Null
    $page = Web GET $editorPath 200
    Assert-Check ($page.Body -match [regex]::Escape([string]$assetId)) 'Pinned header must retain logo reference'
    $draftText = "ترجمه‌ی آزمایشی`nÇeviri metni`n<script>alert(1)</script>"
    $csrf = Csrf $page.Body
    $save = @{ tenantId = $tenantId; jobId = $jobId; expectedRevision = 1; mode = 'text'; plainText = $draftText; __RequestVerificationToken = $csrf }
    Web POST '/Workspace/SaveDraft' 302 (Fields $save) | Out-Null
    $page = Web GET $editorPath 200
    Assert-Check ($page.Body -notmatch '<script>alert\(1\)</script>') 'Draft text must be HTML encoded'
    Assert-Check ([Net.WebUtility]::HtmlDecode($page.Body) -match [regex]::Escape($draftText)) 'Saved multilingual text missing'
    $save.plainText = 'Unsaved conflicting draft'
    $conflict = Web POST '/Workspace/SaveDraft' 409 (Fields $save)
    Assert-Check ($conflict.Body -match 'Unsaved conflicting draft') 'Conflict must preserve user input'
    $documentPath = "$DocumentsUrl/api/v1/tenants/$tenantId/translation-jobs/$jobId/documents"
    $document = Api GET $documentPath $null 200
    Assert-Check ($document.currentDraftRevision -eq 2) 'Conflict must not increment revision'
    $historical = Web GET "$editorPath&revisionNumber=1" 200
    Assert-Check ($historical.Body -match 'readonly="readonly"' -and $historical.Body -match 'disabled="disabled"') 'Historical view must be read-only'
    $other = Api POST "$IdentityUrl/api/v1/tenants" @{ name = 'Other workspace'; slug = "other-web-$runId"; type = 'IndependentTranslator' } 201
    Web GET "/Workspace/Asset?tenantId=$($other.id)&templateId=$templateId&assetId=$assetId" 404 | Out-Null
    Web GET "/Workspace/Editor?tenantId=$($other.id)&jobId=$jobId" 404 | Out-Null
    Web GET "/Workspace/Editor?tenantId=$([Guid]::NewGuid())&jobId=$jobId" 403 | Out-Null
    Web POST '/Account/Logout' 400 | Out-Null
    Web POST '/Account/Logout' 302 (Fields @{ __RequestVerificationToken = (Csrf $page.Body) }) | Out-Null
    Web GET $assetPath 302 | Out-Null
    $badLogin = Web GET '/Account/Login' 200
    $bad = Web POST '/Account/Login' 200 (Fields @{ email = $email; password = 'Wrong-Password!'; __RequestVerificationToken = (Csrf $badLogin.Body) })
    Assert-Check ($bad.Body -notmatch 'Wrong-Password!') 'Password must never be echoed into HTML'
    Write-Output "PASS: $checks web checks. Development tenant: $tenantId"
} finally { $client.Dispose(); $handler.Dispose() }
