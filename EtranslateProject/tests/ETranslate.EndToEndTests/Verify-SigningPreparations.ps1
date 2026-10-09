#Requires -Version 7.0
param(
    [string]$WebUrl = 'https://localhost:7049',
    [string]$IdentityUrl = 'http://localhost:5031',
    [string]$WorkflowUrl = 'http://localhost:5285',
    [string]$DocumentsUrl = 'http://localhost:5241',
    [string]$TrustUrl = 'http://localhost:5033',
    [switch]$EmitBrowserFixture
)
$ErrorActionPreference = 'Stop'
$emitSigningFixture = $EmitBrowserFixture; $signingTrustUrl = $TrustUrl
. "$PSScriptRoot/Verify-TenantTeam.ps1" -WebUrl $WebUrl -IdentityUrl $IdentityUrl -WorkflowUrl $WorkflowUrl -DocumentsUrl $DocumentsUrl
$priorChecks = $checks
$handler = [Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect = $false
$client = [Net.Http.HttpClient]::new($handler); $client.Timeout = [TimeSpan]::FromSeconds(45)
$http = [Net.Http.HttpClient]::new(); $http.Timeout = [TimeSpan]::FromSeconds(45)
$preparationApi = "$signingTrustUrl/api/v1/tenants/$tenantId/translation-jobs/$jobId/documents/$($document.id)/signing-preparations"
$signingPage = "/Workspace/SigningPreparation?tenantId=$tenantId&jobId=$jobId"
function Signing-State($Headers = $bearer) { Api GET $preparationApi $null 200 $Headers }
function Proposal {
    $artifact = (Signing-State).artifact
    return @{ expectedReviewId = $artifact.reviewId; expectedPdfVersionId = $artifact.pdfVersionId; translatorUserId = $translatorId;
        translatorStatement = " Development-only proposal`n<script>not executed</script> "; translatorMethod = 'EImza';
        officeSignerUserId = $adminId; officeStatement = 'Development-only office proposal, not consent'; officeMethod = 'MobilImza' }
}
function Cancel-Preparation($Item, [int]$Status = 200) {
    Api POST "$preparationApi/$($Item.id)/cancel" @{ expectedVersion = $Item.version; reason = 'Development cancellation only' } $Status
}
try {
    Api GET $preparationApi $null 401 @{} | Out-Null
    Api GET $preparationApi $null 403 $guest.Headers | Out-Null
    $initial = Signing-State
    Assert-Check ($initial.artifactServiceAvailable -and $initial.artifact.reviewId -eq $approved.id -and $initial.artifact.sha256 -eq $approved.pdfSha256) 'Signing preflight must pin the exact current approved PDF and verify its archived bytes'
    Assert-Check ($initial.artifact.kind -eq 'DraftUnsigned' -and -not $initial.provider.realSigningEnabled -and -not $initial.provider.eImza -and -not $initial.provider.mobilImza) 'Real providers must be disabled; reference PDF is still unsigned'
    Signing-State $translator.Headers | Out-Null
    Assert-Check (-not (Signing-State $observer.Headers).canManage) 'Read-only members may inspect but not prepare'
    $body = Proposal
    Api POST $preparationApi $body 403 $translator.Headers | Out-Null
    Api POST $preparationApi $body 403 $observer.Headers | Out-Null
    Api POST $preparationApi $body 403 $guest.Headers | Out-Null
    $bad = Proposal; $bad.expectedReviewId = [Guid]::NewGuid(); Api POST $preparationApi $bad 409 | Out-Null
    $bad = Proposal; $bad.expectedPdfVersionId = [Guid]::NewGuid(); Api POST $preparationApi $bad 409 | Out-Null
    $bad = Proposal; $bad.translatorStatement = ' '; Api POST $preparationApi $bad 400 | Out-Null
    $bad = Proposal; $bad.translatorStatement = 'x' + (' ' * 4000); Api POST $preparationApi $bad 400 | Out-Null
    $bad = Proposal; $bad.translatorMethod = 99; Api POST $preparationApi $bad 400 | Out-Null
    $bad = Proposal; $bad.translatorUserId = $observerId; Api POST $preparationApi $bad 400 | Out-Null
    $bad = Proposal; $bad.officeSignerUserId = $translatorId; Api POST $preparationApi $bad 400 | Out-Null
    $bad = Proposal; $bad.officeStatement = ''; Api POST $preparationApi $bad 400 | Out-Null
    $bad = Proposal; $bad.officeSignerUserId = [Guid]::NewGuid(); Api POST $preparationApi $bad 400 | Out-Null
    $external = Register-TestUser 'external-signer'
    $externalTenant = Api POST "$IdentityUrl/api/v1/tenants" @{ name = 'External signer office'; slug = "external-signing-$runId"; type = 'TranslationOffice' } 201 $external.Headers
    $externalActor = Api GET "$IdentityUrl/api/v1/tenants/$($externalTenant.id)/access" $null 200 $external.Headers
    $bad = Proposal; $bad.translatorUserId = $externalActor.userId; Api POST $preparationApi $bad 400 | Out-Null
    $bad = Proposal; $bad.officeSignerUserId = $externalActor.userId; Api POST $preparationApi $bad 400 | Out-Null
    Active $translatorId $false | Out-Null
    Api POST $preparationApi $body 400 | Out-Null
    Api GET $preparationApi $null 403 $translator.Headers | Out-Null
    Active $translatorId $true | Out-Null
    $body.signaturePolicy = 'TranslatorOnly'; $body.pdfSha256 = 'spoofed'; $body.createdByUserId = $guest.Email
    $prepared = Api POST $preparationApi $body 201 $admin.Headers
    Assert-Check ($prepared.createdByUserId -eq $adminId -and $prepared.signaturePolicy -eq 'TranslatorAndTranslationOffice' -and $prepared.pdfSha256 -eq $initial.artifact.sha256) 'Policy, artifact and actor must come from authoritative services, never spoofed fields'
    Assert-Check ($prepared.stages.Count -eq 2 -and $prepared.stages[0].order -eq 1 -and $prepared.stages[1].order -eq 2 -and $prepared.stages[1].signerUserId -eq $adminId) 'Office preparation needs two ordered planned stages'
    Assert-Check ($prepared.stages[0].proposedStatement -ceq $body.translatorStatement -and $prepared.status -eq 'Prepared' -and $prepared.version -eq 0) 'Proposal text must be preserved exactly; preparation is not a signature'
    Api POST $preparationApi $body 409 | Out-Null
    Api POST "$preparationApi/$($prepared.id)/dispatch" $null 501 | Out-Null
    Api POST "$preparationApi/$($prepared.id)/complete" @{ status = 'Signed' } 404 | Out-Null
    Api POST "$preparationApi/$($prepared.id)/cancel" @{ expectedVersion = 1; reason = 'stale' } 409 | Out-Null
    Api POST "$preparationApi/$($prepared.id)/cancel" @{ expectedVersion = 0; reason = '' } 400 | Out-Null
    Api POST "$preparationApi/$($prepared.id)/cancel" @{ expectedVersion = 0; reason = 'x' } 403 $translator.Headers | Out-Null
    $wrongJob = "$signingTrustUrl/api/v1/tenants/$tenantId/translation-jobs/$requiredJobId/documents/$($document.id)/signing-preparations"
    Api GET $wrongJob $null 404 | Out-Null
    Api POST "$wrongJob/$($prepared.id)/cancel" @{ expectedVersion = 0; reason = 'wrong scope' } 404 | Out-Null
    Api GET "$signingTrustUrl/api/v1/tenants/$($other.id)/translation-jobs/$jobId/documents/$($document.id)/signing-preparations" $null 404 | Out-Null
    $cancelled = Cancel-Preparation $prepared
    Assert-Check ($cancelled.status -eq 'Cancelled' -and $cancelled.version -eq 1 -and $cancelled.cancelledByUserId -eq $ownerId -and $cancelled.pdfSha256 -eq $prepared.pdfSha256) 'Cancellation must preserve snapshot and record authenticated actor'
    Cancel-Preparation $cancelled 409 | Out-Null
    $raced = Race $preparationApi (Proposal) $bearer @(201,409)
    Assert-Check (@((Signing-State).preparations | Where-Object status -eq 'Prepared').Count -eq 1) 'Concurrent preparation must not duplicate active requests'
    Api POST "$reviewApi/reviews/$($approved.id)/decision" @{ action = 'Reopen'; note = 'Reopen development artifact' } 200 $admin.Headers | Out-Null
    $outdated = (Signing-State).preparations | Where-Object id -eq $raced.id
    Assert-Check (-not $outdated.isCurrentArtifact -and $outdated.status -eq 'Prepared' -and $outdated.pdfSha256 -eq $prepared.pdfSha256) 'Reopening approval must make old request outdated without changing its history'
    Api POST $preparationApi $body 409 | Out-Null
    Cancel-Preparation $outdated | Out-Null
    $draft4 = Api POST "$reviewApi/draft-revisions" @{ expectedCurrentRevision = 3; editorContentJson = '{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Updated development translation"}]}]}' } 201 $translator.Headers
    $pdf4 = Api POST "$reviewApi/draft-revisions/4/pdfs" $null 201 $translator.Headers
    $review4 = Api POST "$reviewApi/reviews" @{ expectedRevision = 4; expectedRound = 1; pdfVersionId = $pdf4.id } 201 $translator.Headers
    Api POST "$reviewApi/reviews/$($review4.id)/decision" @{ action = 'Approve' } 200 $admin.Headers | Out-Null
    Assert-Check ((Signing-State).artifact.sha256 -eq $pdf4.sha256 -and $pdf4.sha256 -ne $prepared.pdfSha256) 'Updated approval must reference a new immutable PDF'
    $updated = Api POST $preparationApi (Proposal) 201
    Assert-Check ($updated.revisionNumber -eq 4 -and $updated.reviewRound -eq 2 -and (Signing-State).preparations.Count -eq 3) 'Reissue must preserve older cancelled requests'
    Race "$preparationApi/$($updated.id)/cancel" @{ expectedVersion = 0; reason = 'Development concurrent cancellation' } $bearer @(200,409) | Out-Null
    Assert-Check (@((Signing-State).preparations | Where-Object status -eq 'Prepared').Count -eq 0) 'Concurrent cancellation must leave no active request and only one terminal transition'
    # Independent tenant needs only a translator stage, even when notary is not required.
    $independentJobs = "$WorkflowUrl/api/v1/tenants/$($other.id)/translation-jobs"
    $independentJob = Api POST $independentJobs @{ title = 'Independent signing preparation'; sourceLanguageCode = 'en'; targetLanguageCode = 'tr'; notaryRequirement = 'NotRequired' } 201
    $independentDocuments = "$DocumentsUrl/api/v1/tenants/$($other.id)/translation-jobs/$($independentJob.id)/documents"
    $independentDocument = Api POST $independentDocuments $null 201
    $independentApi = "$independentDocuments/$($independentDocument.id)"
    $independentTrust = "$signingTrustUrl/api/v1/tenants/$($other.id)/translation-jobs/$($independentJob.id)/documents/$($independentDocument.id)/signing-preparations"
    $noApproval = Api GET "$independentApi/signing-artifact" $null 200
    Assert-Check ($null -eq $noApproval.artifact -and $noApproval.reviewStatus -eq 'Draft') 'No-review document must not have an approved artifact'
    Api POST $independentTrust $body 409 | Out-Null
    Api POST "$independentApi/draft-revisions" @{ expectedCurrentRevision = 0; editorContentJson = '{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Independent development translation"}]}]}' } 201 | Out-Null
    $independentPdf = Api POST "$independentApi/draft-revisions/1/pdfs" $null 201
    $independentReview = Api POST "$independentApi/reviews" @{ expectedRevision = 1; expectedRound = 0; pdfVersionId = $independentPdf.id } 201
    Api POST "$independentApi/reviews/$($independentReview.id)/decision" @{ action = 'Approve' } 200 | Out-Null
    $solo = @{ expectedReviewId = $independentReview.id; expectedPdfVersionId = $independentPdf.id; translatorUserId = $ownerId; translatorStatement = 'Development solo proposal'; translatorMethod = 'MobilImza' }
    $solo.officeSignerUserId = $ownerId; Api POST $independentTrust $solo 400 | Out-Null; $solo.Remove('officeSignerUserId')
    $soloPrepared = Api POST $independentTrust $solo 201
    Assert-Check ($soloPrepared.signaturePolicy -eq 'TranslatorOnly' -and $soloPrepared.stages.Count -eq 1) 'Independent workflow must omit office stage'
    # Web BFF: authorization, CSRF, XSS, preserved invalid input, immutable history.
    $loginPage = Web GET '/Account/Login' 200
    Web POST '/Account/Login' 302 (Fields @{ email = $email; password = $password; __RequestVerificationToken = (Csrf $loginPage.Body) }) | Out-Null
    $page = Web GET $signingPage 200
    Assert-Check ($page.Body -match 'Form.TranslatorStatement' -and $page.Body -match 'Form.OfficeStatement' -and $page.Body -match 'disabled') 'Manager page must show proposal fields and disabled real signing action'
    $webBody = @{ tenantId = $tenantId; jobId = $jobId; 'Form.ExpectedReviewId' = $review4.id; 'Form.ExpectedPdfVersionId' = $pdf4.id; 'Form.TranslatorUserId' = $translatorId;
        'Form.TranslatorStatement' = 'Development-only <script>not executed</script>'; 'Form.TranslatorMethod' = 'EImza'; 'Form.OfficeSignerUserId' = $translatorId;
        'Form.OfficeStatement' = 'Office proposal'; 'Form.OfficeMethod' = 'MobilImza' }
    Web POST '/Workspace/PrepareSigning' 400 (Fields $webBody) | Out-Null
    $webBody.__RequestVerificationToken = Csrf $page.Body
    $invalid = Web POST '/Workspace/PrepareSigning' 400 (Fields $webBody)
    Assert-Check ($invalid.Body -notmatch '<script>not executed</script>' -and [Net.WebUtility]::HtmlDecode($invalid.Body) -match [regex]::Escape($webBody['Form.TranslatorStatement'])) 'Invalid proposal must preserve text without executing HTML'
    $webBody['Form.OfficeSignerUserId'] = $adminId
    Web POST '/Workspace/PrepareSigning' 302 (Fields $webBody) | Out-Null
    $page = Web GET $signingPage 200
    Assert-Check ($page.Body -notmatch '<script>not executed</script>' -and $page.Headers.CacheControl.NoStore) 'Preparation history must encode proposed text and be private/no-store'
    $conflict = Web POST '/Workspace/PrepareSigning' 409 (Fields $webBody)
    Assert-Check ([Net.WebUtility]::HtmlDecode($conflict.Body) -match [regex]::Escape($webBody['Form.TranslatorStatement'])) 'Conflicting request must retain unregistered proposal text'
    $active = (Signing-State).preparations | Where-Object status -eq 'Prepared'
    Web POST '/Workspace/CancelSigningPreparation' 400 (Fields @{ tenantId = $tenantId; jobId = $jobId; preparationId = $active.id; expectedVersion = 0; reason = 'test' }) | Out-Null
    Web POST '/Workspace/CancelSigningPreparation' 302 (Fields @{ tenantId = $tenantId; jobId = $jobId; preparationId = $active.id; expectedVersion = 0; reason = 'Development web cancellation'; __RequestVerificationToken = (Csrf $page.Body) }) | Out-Null
    Assert-Check (@((Signing-State).preparations | Where-Object status -eq 'Prepared').Count -eq 0) 'Web cancellation must persist'
    Web POST '/Account/Logout' 302 (Fields @{ __RequestVerificationToken = (Csrf $page.Body) }) | Out-Null
    Web GET $signingPage 302 | Out-Null
    $loginPage = Web GET '/Account/Login' 200
    Web POST '/Account/Login' 302 (Fields @{ email = $translator.Email; password = $translator.Password; __RequestVerificationToken = (Csrf $loginPage.Body) }) | Out-Null
    $page = Web GET $signingPage 200
    Assert-Check ($page.Body -notmatch '<form[^>]+action="/Workspace/PrepareSigning' -and $page.Body -notmatch '<form[^>]+action="/Workspace/CancelSigningPreparation') 'Translator must not have mutation forms'
    $webBody.__RequestVerificationToken = Csrf $page.Body
    Web POST '/Workspace/PrepareSigning' 403 (Fields $webBody) | Out-Null
    $prepared = Api POST $preparationApi (Proposal) 201
    Write-Output "PASS: $($checks - $priorChecks) signing-preparation checks plus $priorChecks baseline/team checks. Development tenant: $tenantId"
    if ($emitSigningFixture) { Write-Output (ConvertTo-Json @{ email = $email; password = $password; tenantId = $tenantId; jobId = $jobId; preparationId = $prepared.id } -Compress) }
} finally { $http.Dispose(); $client.Dispose(); $handler.Dispose() }
