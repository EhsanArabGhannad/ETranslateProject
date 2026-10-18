#Requires -Version 7.0
param([string]$WebUrl='http://localhost:5067', [string]$IdentityUrl='http://localhost:5031',
    [string]$WorkflowUrl='http://localhost:5285', [string]$DocumentsUrl='http://localhost:5241', [string]$BillingUrl='http://localhost:5249')
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Verify-Navigation.ps1" -WebUrl $WebUrl -IdentityUrl $IdentityUrl -WorkflowUrl $WorkflowUrl -DocumentsUrl $DocumentsUrl -BillingUrl $BillingUrl
$editingBaselineChecks=$checks
$handler=[Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect=$false
$client=[Net.Http.HttpClient]::new($handler); $client.Timeout=[TimeSpan]::FromSeconds(45)
function Template-Fields($Id,$Version,$Name) {
    return @{'Form.TenantId'=$tenantId;'Form.TemplateId'=$Id;'Form.ExpectedRevision'=$Version;'Form.Name'=$Name;'Form.Header'='Edited header';'Form.Footer'='Edited footer';'Form.StarterText'='Starter text';'Form.Watermark'='DRAFT';'Form.PageSize'='A4';'Form.Orientation'='Portrait';'Form.MarginTop'=25;'Form.MarginRight'=20;'Form.MarginBottom'=20;'Form.MarginLeft'=20;'Form.LogoMode'='Add'}
}
function Save-Template($Values,$Page,[int]$Status=302,[byte[]]$Image=$null,[bool]$CsrfEnabled=$true) {
    $data=[Net.Http.MultipartFormDataContent]::new()
    foreach ($key in $Values.Keys) { $data.Add([Net.Http.StringContent]::new([string]$Values[$key]),$key) }
    if ($CsrfEnabled) { $data.Add([Net.Http.StringContent]::new((Csrf $Page.Body)),'__RequestVerificationToken') }
    if ($null -ne $Image) { $content=[Net.Http.ByteArrayContent]::new($Image); $content.Headers.ContentType=[Net.Http.Headers.MediaTypeHeaderValue]::new('image/png'); $data.Add($content,'logo','logo.png') }
    return Web POST '/Settings/SaveTemplate' $Status $data
}
try {
    Login-Navigation $email $password
    $page=Web GET "/Settings/Templates?tenantId=$tenantId" 200
    $name="Unified-$runId"; $values=Template-Fields '' 0 $name
    Save-Template $values $page 400 $null $false | Out-Null
    # Use the known-valid native PNG fixture from the baseline.
    [byte[]]$logo=$png
    $created=Save-Template $values $page 302 $logo
    $id=[regex]::Match($created.Location,'templateId=([a-f0-9-]+)').Groups[1].Value
    Assert-Check ($id -and $created.Location -match "/Settings/Templates\?tenantId=$tenantId") 'One create form redirects to its new template identity'
    $path="$DocumentsUrl/api/v1/tenants/$tenantId/document-templates/$id"
    $detail=Api GET $path $null 200
    Assert-Check ($detail.currentRevision -eq 2 -and $detail.name -eq $name) 'Create plus logo completes the two persisted stages'
    $original=$detail.revision
    $originalAsset=(ConvertFrom-Json $original.headerContentJson).content[-1].attrs.assetId
    $job=Api POST "$WorkflowUrl/api/v1/tenants/$tenantId/translation-jobs" @{title='Pinned template editing fixture';sourceLanguageCode='fa';targetLanguageCode='tr';notaryRequirement='NotRequired'} 201
    $docPath="$DocumentsUrl/api/v1/tenants/$tenantId/translation-jobs/$($job.id)/documents"
    $doc=Api POST $docPath $null 201
    Api POST "$docPath/$($doc.id)/template" @{templateId=$id;revisionNumber=2} 200 | Out-Null
    $page=Web GET $created.Location 200
    Assert-Check ([Net.WebUtility]::HtmlDecode($page.Body) -match 'Edited header' -and $page.Body -match 'id="template-picker"' -and $page.Body -match 'name="Form.ExpectedRevision"[^>]*value="2"') 'Selected template loads text and optimistic revision'
    $values=Template-Fields $id 2 $name; $values['Form.Header']='Replacement header'; $values['Form.LogoMode']='Replace'
    Save-Template $values $page 302 $logo | Out-Null
    $updated=Api GET $path $null 200
    $images=@((ConvertFrom-Json $updated.revision.headerContentJson).content | Where-Object type -eq 'image')
    Assert-Check ($updated.currentRevision -eq 3 -and $images.Count -eq 1 -and $images[0].attrs.assetId -ne $originalAsset) 'Edit with explicit image replacement creates one revision and one image'
    $old=Api GET "$path/revisions/2" $null 200
    Assert-Check ($old.headerContentJson -eq $original.headerContentJson -and $old.editorContentJson -eq $original.editorContentJson) 'Historical template bytes are untouched'
    $pinned=Api GET "$docPath/$($doc.id)/template" $null 200
    Assert-Check ($pinned.id -eq $original.id -and $pinned.headerContentJson -eq $original.headerContentJson) 'Existing document stays on its exact pinned layout'
    $beforeAssets=@(Api GET "$path/assets" $null 200).Count
    Save-Template $values $page 409 $logo | Out-Null
    Assert-Check (@(Api GET "$path/assets" $null 200).Count -eq $beforeAssets -and (Api GET $path $null 200).currentRevision -eq 3) 'Stale form does not upload or create a revision'
    # Failed PNG signature AFTER initial create must recover to the same identity, not create again.
    $badName="Partial-$runId"; $newPage=Web GET "/Settings/Templates?tenantId=$tenantId" 200
    $partial=Save-Template (Template-Fields '' 0 $badName) $newPage 400 ([byte[]](1,2,3,4))
    $partialId=[regex]::Match($partial.Body,'name="Form.TemplateId"[^>]*value="([a-f0-9-]+)"').Groups[1].Value
    Assert-Check ([Guid]::TryParse($partialId,[ref]([Guid]::Empty))) 'Partial create returns its persisted template ID'
    Assert-Check ([Net.WebUtility]::HtmlDecode($partial.Body) -match 'قالب ساخته شده') 'Partial failure is not reported as full success'
    Save-Template (Template-Fields $partialId 1 $badName) $partial 302 $logo | Out-Null
    $matching=@(Api GET "$DocumentsUrl/api/v1/tenants/$tenantId/document-templates" $null 200 | Where-Object name -eq $badName)
    Assert-Check ($matching.Count -eq 1 -and $matching[0].id -eq $partialId -and $matching[0].currentRevision -eq 2) 'Retry uses the existing identity, not another template'
    # Reuse an uploaded asset, scoped by the server; a forged asset ID is rejected.
    $page=Web GET "/Settings/Templates?tenantId=$tenantId&templateId=$id" 200
    $reuse=Template-Fields $id 3 $name; $reuse['Form.PendingAssetId']=[Guid]::NewGuid()
    Save-Template $reuse $page 400 | Out-Null
    Assert-Check ((Api GET $path $null 200).currentRevision -eq 3) 'Forged recovery references do not mutate template'
    $reuse['Form.PendingAssetId']=$originalAsset
    Save-Template $reuse $page 302 | Out-Null
    $page=Web GET "/Settings/Templates?tenantId=$tenantId&templateId=$id&pendingAssetId=$originalAsset" 200
    Assert-Check ($page.Body -match 'در انتظار ثبت' -or [Net.WebUtility]::HtmlDecode($page.Body) -match 'بارگذاری شده') 'Verified recovery image can be reviewed in a fresh page'
    $reuse['Form.ExpectedRevision']=4
    Save-Template $reuse $page 302 | Out-Null
    $replayed=Api GET $path $null 200
    Assert-Check (@((ConvertFrom-Json $replayed.revision.headerContentJson).content | Where-Object type -eq 'image').Count -eq 2) 'Explicit reuse does not duplicate an already referenced image'
    $rich='{"type":"doc","content":[{"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Rich starter","marks":[{"type":"bold"}]}]}]}'
    $complex=Api POST "$DocumentsUrl/api/v1/tenants/$tenantId/document-templates" @{name="Rich-$runId";editorContentJson=$rich;headerContentJson='[]';footerContentJson='[]';pageLayoutJson='{"custom":true}';watermarkJson='{"text":"Keep","custom":true}'} 201
    $page=Web GET "/Settings/Templates?tenantId=$tenantId&templateId=$($complex.id)" 200
    $form=Template-Fields $complex.id 1 $complex.name; $form['Form.StarterText']='Must not flatten'; $form['Form.Header']='Must not flatten'
    Save-Template $form $page 302 | Out-Null
    $preserved=Api GET "$DocumentsUrl/api/v1/tenants/$tenantId/document-templates/$($complex.id)" $null 200
    Assert-Check ($preserved.revision.editorContentJson -eq $rich -and $preserved.revision.headerContentJson -eq '[]' -and $preserved.revision.pageLayoutJson -eq '{"custom":true}') 'Unsupported parts survive even forged plain-text inputs'
    # Presentation settings persist in immutable, explicitly typed header/footer regions.
    $page=Web GET "/Settings/Templates?tenantId=$tenantId&templateId=$id" 200
    $presentation=Template-Fields $id 5 $name
    $presentation['Form.Header']="Office`nنشانی آزمایشی"; $presentation['Form.HeaderLineAlignmentsJson']='["center","right"]'
    $presentation['Form.FooterLineAlignmentsJson']='["left"]'; $presentation['Form.ImageLayout']='banner'; $presentation['Form.ImageAlignment']='right'
    $presentation['Form.LogoMode']='Replace'
    Save-Template $presentation $page 302 $logo | Out-Null
    $formatted=Api GET $path $null 200
    $formattedHeader=ConvertFrom-Json $formatted.revision.headerContentJson
    Assert-Check ($formatted.currentRevision -eq 6 -and $formattedHeader.type -eq 'header' -and $formattedHeader.content[0].attrs.textAlign -eq 'center' -and $formattedHeader.content[1].attrs.textAlign -eq 'right') 'Independent line alignment is persisted without changing the body schema'
    Assert-Check (@($formattedHeader.content | Where-Object { $_.type -eq 'image' -and $_.attrs.layout -eq 'banner' -and $_.attrs.align -eq 'right' }).Count -eq 1) 'Banner layout and image alignment apply to the new revision only'
    $page=Web GET "/Settings/Templates?tenantId=$tenantId&templateId=$id" 200
    Assert-Check ($page.Body -match 'value="banner" selected="selected"' -or $page.Body -match 'selected="selected" value="banner"') 'Existing image presentation fills its selector on reload'
    $invalid=Template-Fields '' 0 "Invalid-alignment-$runId"; $invalid['Form.HeaderLineAlignmentsJson']='["right; color:red"]'
    Save-Template $invalid $page 400 $logo | Out-Null
    # Image-only full-width header with the native square baseline fixture stays below the height limit.
    $presentation['Form.ExpectedRevision']=6; $presentation['Form.Header']=''; $presentation['Form.HeaderLineAlignmentsJson']='[]'
    Save-Template $presentation $page 302 | Out-Null
    $imageOnly=Api GET $path $null 200
    Assert-Check ((ConvertFrom-Json $imageOnly.revision.headerContentJson).content.Count -eq 1) 'Image-only banner does not insert a blank text paragraph'
    $pdfJob=Api POST "$WorkflowUrl/api/v1/tenants/$tenantId/translation-jobs" @{title='Presentation PDF';sourceLanguageCode='tr';targetLanguageCode='fa';notaryRequirement='NotRequired'} 201
    $pdfCollection="$DocumentsUrl/api/v1/tenants/$tenantId/translation-jobs/$($pdfJob.id)/documents"
    $pdfDocument=Api POST $pdfCollection $null 201; $pdfDocumentPath="$pdfCollection/$($pdfDocument.id)"
    Api POST "$pdfDocumentPath/template" @{templateId=$id;revisionNumber=7} 200 | Out-Null
    $pdfVersion=Api POST "$pdfDocumentPath/draft-revisions/1/pdfs" $null 201
    Assert-Check ($pdfVersion.templateRevisionId -eq $imageOnly.revision.id -and $pdfVersion.rendererVersion -match '^draft-html-v10-footer-policy') 'Authorized PDF pipeline resolves the exact formatted template and new renderer version'
    $cached=Api POST "$pdfDocumentPath/draft-revisions/1/pdfs" $null 200
    Assert-Check ($cached.id -eq $pdfVersion.id -and $cached.sha256 -eq $pdfVersion.sha256) 'New renderer cache preserves the archived PDF identity'
    Web GET "/Settings/Templates?tenantId=$($solo.id)&templateId=$id" 404 | Out-Null
    Web POST '/Account/Logout' 302 (Fields @{__RequestVerificationToken=(Csrf $page.Body)}) | Out-Null
    Login-Navigation $observer.Email $observer.Password
    $page=Web GET "/Settings/Templates?tenantId=$tenantId&templateId=$id" 200
    Save-Template (Template-Fields $id 5 $name) $page 403 $logo | Out-Null
    $browserNames=@('ETRANSLATE_TEMPLATE_BROWSER_TESTS','ETRANSLATE_NAV_WEB_URL','ETRANSLATE_NAV_TENANT','ETRANSLATE_NAV_EMAIL','ETRANSLATE_NAV_PASSWORD','ETRANSLATE_NAV_QA_PATH','PLAYWRIGHT_BROWSERS_PATH')
    $oldBrowser=@{}; foreach ($key in $browserNames) { $oldBrowser[$key]=[Environment]::GetEnvironmentVariable($key) }
    try {
        $env:ETRANSLATE_TEMPLATE_BROWSER_TESTS='1'; $env:ETRANSLATE_NAV_WEB_URL=$WebUrl; $env:ETRANSLATE_NAV_TENANT=$tenantId
        $env:ETRANSLATE_NAV_EMAIL=$email; $env:ETRANSLATE_NAV_PASSWORD=$password
        $env:ETRANSLATE_NAV_QA_PATH=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../tmp/navigation-qa'))
        $env:PLAYWRIGHT_BROWSERS_PATH=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../.local/pdf-browsers'))
        & dotnet test (Join-Path $PSScriptRoot '../ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-build --no-restore --filter 'FullyQualifiedName~TemplateFormBrowserTests' --logger 'console;verbosity=normal'
        Assert-Check ($LASTEXITCODE -eq 0) 'Unified form: browser selection, image preview, creation, editing, reload and mobile layout pass'
    } finally { foreach ($key in $browserNames) { [Environment]::SetEnvironmentVariable($key,$oldBrowser[$key]) } }
    Write-Output "PASS: $($checks-$editingBaselineChecks) template editing checks plus $editingBaselineChecks navigation/team baseline checks. Retained fixture: $tenantId"
} finally { $client.Dispose(); $handler.Dispose() }
