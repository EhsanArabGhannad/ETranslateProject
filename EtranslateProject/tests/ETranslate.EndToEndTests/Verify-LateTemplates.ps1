#Requires -Version 7.0
param([string]$WebUrl='http://localhost:5067', [string]$IdentityUrl='http://localhost:5031',
    [string]$WorkflowUrl='http://localhost:5285', [string]$DocumentsUrl='http://localhost:5241', [string]$BillingUrl='http://localhost:5249')
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Verify-Navigation.ps1" -WebUrl $WebUrl -IdentityUrl $IdentityUrl -WorkflowUrl $WorkflowUrl -DocumentsUrl $DocumentsUrl -BillingUrl $BillingUrl
$lateBaseline=$checks
$handler=[Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect=$false
$client=[Net.Http.HttpClient]::new($handler); $client.Timeout=[TimeSpan]::FromSeconds(45)
function Late-Document($Title) {
    $j=Api POST "$WorkflowUrl/api/v1/tenants/$tenantId/translation-jobs" @{title=$Title;sourceLanguageCode='tr';targetLanguageCode='fa';notaryRequirement='NotRequired'} 201
    $collection="$DocumentsUrl/api/v1/tenants/$tenantId/translation-jobs/$($j.id)/documents"
    $d=Api POST $collection $null 201
    return @{Job=$j;Document=$d;Path="$collection/$($d.id)"}
}
try {
    Login-Navigation $email $password
    $a=Api GET "$DocumentsUrl/api/v1/tenants/$tenantId/document-templates/$templateId" $null 200
    $b=Api POST "$DocumentsUrl/api/v1/tenants/$tenantId/document-templates" @{name="Late B $runId";editorContentJson='{"type":"doc","content":[]}';headerContentJson='{"type":"header","content":[{"type":"paragraph","content":[{"type":"text","text":"NEW TEMPLATE B"}]}]}';footerContentJson='{"type":"footer","content":[{"type":"paragraph","content":[{"type":"text","text":"B FOOTER"}]}]}';pageLayoutJson='{"pageSize":"A4","orientation":"Portrait","marginsMm":{"top":25,"right":20,"bottom":20,"left":20},"footerPlacement":"LastPage"}'} 201
    $f=Late-Document 'Late template body preservation'; $dp=$f.Path; $lateJobId=$f.Job.id
    $body='{ "type":"doc", "content":[{"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"SAVED TRANSLATION BODY","marks":[{"type":"bold"}]}]},{"type":"paragraph","content":[{"type":"text","text":"Türkçe çeviri korunur."}]}] }'
    Api POST "$dp/draft-revisions" @{expectedCurrentRevision=0;editorContentJson=$body;plainText='Exact saved text'} 201 | Out-Null
    Api POST "$dp/draft-revisions" @{expectedCurrentRevision=1;editorContentJson=$body;plainText='Exact saved text'} 201 | Out-Null
    $oldPdf=Api POST "$dp/draft-revisions/2/pdfs" $null 201
    $page=Web GET "/Workspace/DocumentTemplate?tenantId=$tenantId&jobId=$lateJobId" 200
    $fields=@{tenantId=$tenantId;jobId=$lateJobId;templateId=$a.id;revisionNumber=2;expectedRevision=2}
    Web POST '/Workspace/ChangeDocumentTemplate' 400 (Fields $fields) | Out-Null
    $fields.__RequestVerificationToken=Csrf $page.Body
    Web POST '/Workspace/ChangeDocumentTemplate' 302 (Fields $fields) | Out-Null
    $third=Api GET "$dp/draft-revisions/3" $null 200
    Assert-Check ($third.editorContentJson -eq $body -and $third.plainText -eq 'Exact saved text' -and $third.templateRevisionId -eq $a.revision.id) 'Late attach preserves exact body/plain text and creates a new immutable template snapshot'
    Api POST "$dp/template" @{templateId=$a.id;revisionNumber=2} 409 | Out-Null
    Api POST "$dp/template-change" @{templateId=$a.id;revisionNumber=2;expectedCurrentRevision=3} 409 | Out-Null
    Api POST "$dp/template-change" @{templateId=$b.id;revisionNumber=1;expectedCurrentRevision=2} 409 | Out-Null
    Api POST "$dp/template-change" @{templateId=$b.id;revisionNumber=1;expectedCurrentRevision=3} 200 | Out-Null
    $fourth=Api GET "$dp/draft-revisions/4" $null 200
    Assert-Check ($fourth.editorContentJson -eq $body -and $fourth.templateRevisionId -eq $b.revision.id) 'Replacement preserves content and snapshots the new template'
    $historical=Api GET "$dp/template?draftRevisionNumber=3" $null 200
    Assert-Check ($historical.id -eq $a.revision.id -and $historical.headerContentJson -eq $a.revision.headerContentJson) 'Historical template endpoint uses draft snapshot, not current document pin'
    Api GET "$dp/template?draftRevisionNumber=1" $null 404 | Out-Null
    Api GET "$dp/template?draftRevisionNumber=999" $null 404 | Out-Null
    Api GET "$dp/template?draftRevisionNumber=0" $null 400 | Out-Null
    $firstPdf=Api POST "$dp/draft-revisions/1/pdfs" $null 201
    $thirdPdf=Api POST "$dp/draft-revisions/3/pdfs" $null 201
    $fourthPdf=Api POST "$dp/draft-revisions/4/pdfs" $null 201
    Assert-Check ($null -eq $firstPdf.templateRevisionId -and $thirdPdf.templateRevisionId -eq $a.revision.id -and $fourthPdf.templateRevisionId -eq $b.revision.id) 'Previously unrendered historical PDFs use no-template/A/B snapshots correctly'
    $cached=Api POST "$dp/draft-revisions/2/pdfs" $null 200
    Assert-Check ($cached.id -eq $oldPdf.id -and $cached.sha256 -eq $oldPdf.sha256 -and $null -eq $cached.templateRevisionId) 'Already archived PDF identity is unchanged by later template changes'
    $qa=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../tmp/late-template-qa')); [IO.Directory]::CreateDirectory($qa) | Out-Null
    foreach ($sample in @(@{Name='historical-no-template';Pdf=$firstPdf},@{Name='historical-a';Pdf=$thirdPdf},@{Name='current-b';Pdf=$fourthPdf})) {
        [byte[]]$bytes=$client.GetByteArrayAsync("$WebUrl/Workspace/DraftPdf?tenantId=$tenantId&jobId=$lateJobId&pdfId=$($sample.Pdf.id)").GetAwaiter().GetResult()
        Assert-Check ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() -eq $sample.Pdf.sha256) 'Downloaded historical/current PDF matches archived hash'
        [IO.File]::WriteAllBytes((Join-Path $qa "$($sample.Name).pdf"),$bytes)
    }
    # Archived/foreign templates, readers, anonymous callers and unowned scope cannot mutate.
    Api PUT "$DocumentsUrl/api/v1/tenants/$tenantId/document-templates/$($a.id)/status" @{isActive=$false} 200 | Out-Null
    Api POST "$dp/template-change" @{templateId=$a.id;revisionNumber=2;expectedCurrentRevision=4} 409 | Out-Null
    Api PUT "$DocumentsUrl/api/v1/tenants/$tenantId/document-templates/$($a.id)/status" @{isActive=$true} 200 | Out-Null
    Api POST "$dp/template-change" @{templateId=$b.id;revisionNumber=1;expectedCurrentRevision=4} 403 $observer.Headers | Out-Null
    Api POST "$dp/template-change" @{templateId=$b.id;revisionNumber=1;expectedCurrentRevision=4} 401 @{} | Out-Null
    Api POST "$DocumentsUrl/api/v1/tenants/$($solo.id)/translation-jobs/$lateJobId/documents/$($f.Document.id)/template-change" @{templateId=$b.id;revisionNumber=1;expectedCurrentRevision=4} 404 | Out-Null
    # Image references in body cannot be stranded by switching to another template.
    $imageCase=Late-Document 'Body image boundary'
    Api POST "$($imageCase.Path)/template" @{templateId=$a.id;revisionNumber=2} 200 | Out-Null
    $imageJson="{`"type`":`"doc`",`"content`":[{`"type`":`"image`",`"attrs`":{`"assetId`":`"$assetId`"}}]}"
    Api POST "$($imageCase.Path)/draft-revisions" @{expectedCurrentRevision=1;editorContentJson=$imageJson;plainText=$null} 201 | Out-Null
    Api POST "$($imageCase.Path)/template-change" @{templateId=$b.id;revisionNumber=1;expectedCurrentRevision=2} 400 | Out-Null
    $a3=Api POST "$DocumentsUrl/api/v1/tenants/$tenantId/document-templates/$($a.id)/revisions" @{expectedCurrentRevision=2;editorContentJson=$a.revision.editorContentJson;headerContentJson=$a.revision.headerContentJson;footerContentJson=$a.revision.footerContentJson;pageLayoutJson=$a.revision.pageLayoutJson;watermarkJson=$a.revision.watermarkJson} 201
    Api POST "$($imageCase.Path)/template-change" @{templateId=$a.id;revisionNumber=3;expectedCurrentRevision=2} 200 | Out-Null
    Assert-Check ((Api GET "$($imageCase.Path)/draft-revisions/3" $null 200).editorContentJson -eq $imageJson) 'Same-template revision upgrade keeps managed body images exact'
    $review=Api POST "$dp/reviews" @{expectedRevision=4;expectedRound=0;pdfVersionId=$fourthPdf.id} 201
    Api POST "$dp/template-change" @{templateId=$a.id;revisionNumber=3;expectedCurrentRevision=4} 409 | Out-Null
    Api POST "$dp/reviews/$($review.id)/decision" @{action='Approve';note='Synthetic approval'} 200 | Out-Null
    Api POST "$dp/template-change" @{templateId=$a.id;revisionNumber=3;expectedCurrentRevision=4} 409 | Out-Null
    Api GET "$dp/signing-artifact" $null 200 | Out-Null
    Api POST "$dp/reviews/$($review.id)/decision" @{action='Reopen';note='Change layout explicitly'} 200 | Out-Null
    Api POST "$dp/template-change" @{templateId=$a.id;revisionNumber=3;expectedCurrentRevision=4} 200 | Out-Null
    $artifact=Api GET "$dp/signing-artifact" $null 200
    Assert-Check ($null -eq $artifact.artifact -and $artifact.reviewStatus -ne 'Approved') 'Reopened/changed draft exposes no approved signing artifact'
    Api POST "$dp/reviews" @{expectedRevision=5;expectedRound=1;pdfVersionId=$fourthPdf.id} 404 | Out-Null
    $fields.expectedRevision=4; Web POST '/Workspace/ChangeDocumentTemplate' 302 (Fields $fields) | Out-Null
    Assert-Check ((Api GET "$DocumentsUrl/api/v1/tenants/$tenantId/translation-jobs/$lateJobId/documents" $null 200).currentDraftRevision -eq 5) 'Stale Web form does not create another draft'
    $names=@('ETRANSLATE_LATE_BROWSER_TESTS','ETRANSLATE_NAV_WEB_URL','ETRANSLATE_NAV_TENANT','ETRANSLATE_NAV_EMAIL','ETRANSLATE_NAV_PASSWORD','ETRANSLATE_NAV_QA_PATH','PLAYWRIGHT_BROWSERS_PATH','ETRANSLATE_LATE_JOB','ETRANSLATE_LATE_TARGET','ETRANSLATE_LATE_OLD_TEMPLATE','ETRANSLATE_TEMPLATE_SQL_TESTS')
    $saved=@{}; foreach ($key in $names) { $saved[$key]=[Environment]::GetEnvironmentVariable($key) }
    try {
        $env:ETRANSLATE_LATE_BROWSER_TESTS='1'; $env:ETRANSLATE_NAV_WEB_URL=$WebUrl; $env:ETRANSLATE_NAV_TENANT=$tenantId
        $env:ETRANSLATE_NAV_EMAIL=$email; $env:ETRANSLATE_NAV_PASSWORD=$password; $env:ETRANSLATE_NAV_QA_PATH=$qa
        $env:PLAYWRIGHT_BROWSERS_PATH=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../.local/pdf-browsers'))
        $env:ETRANSLATE_LATE_JOB=$lateJobId; $env:ETRANSLATE_LATE_TARGET=$b.id; $env:ETRANSLATE_LATE_OLD_TEMPLATE=$a.id
        $env:ETRANSLATE_TEMPLATE_SQL_TESTS='1'
        & dotnet test (Join-Path $PSScriptRoot '../ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-build --no-restore --filter 'FullyQualifiedName~LateTemplateBrowserTests|FullyQualifiedName~LateTemplateSqlTests' --logger 'console;verbosity=normal'
        Assert-Check ($LASTEXITCODE -eq 0) 'Separate tab/history/unsaved text browser test and SQL race checks pass'
    } finally { foreach ($key in $names) { [Environment]::SetEnvironmentVariable($key,$saved[$key]) } }
    Write-Output "PASS: $($checks-$lateBaseline) late-template checks plus $lateBaseline baseline/navigation checks. Retained fixture: $tenantId"
} finally { $client.Dispose(); $handler.Dispose() }
