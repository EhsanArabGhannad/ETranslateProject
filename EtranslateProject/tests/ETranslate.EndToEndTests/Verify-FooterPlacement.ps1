#Requires -Version 7.0
param([string]$WebUrl='http://localhost:5067', [string]$IdentityUrl='http://localhost:5031',
    [string]$WorkflowUrl='http://localhost:5285', [string]$DocumentsUrl='http://localhost:5241', [string]$BillingUrl='http://localhost:5249', [string]$PythonPath='python')
$ErrorActionPreference='Stop'
$footerPythonTool=(Get-Command $PythonPath -ErrorAction Stop).Source
& $footerPythonTool -c 'import pypdf'
if ($LASTEXITCODE -ne 0) { throw 'Footer PDF checks require Python with pypdf.' }
. "$PSScriptRoot/Verify-TemplateEditing.ps1" -WebUrl $WebUrl -IdentityUrl $IdentityUrl -WorkflowUrl $WorkflowUrl -DocumentsUrl $DocumentsUrl -BillingUrl $BillingUrl
$footerBaselineChecks=$checks
$handler=[Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect=$false
$client=[Net.Http.HttpClient]::new($handler); $client.Timeout=[TimeSpan]::FromSeconds(45)
try {
    Login-Navigation $email $password
    $page=Web GET "/Settings/Templates?tenantId=$tenantId&templateId=$id" 200
    $values=Template-Fields $id 7 $name; $values['Form.FooterPlacement']='LastPage'
    Save-Template $values $page 302 | Out-Null
    $last=Api GET $path $null 200
    Assert-Check ($last.currentRevision -eq 8 -and (ConvertFrom-Json $last.revision.pageLayoutJson).footerPlacement -eq 'LastPage') 'Last-page policy is stored in the new immutable layout revision'
    $page=Web GET "/Settings/Templates?tenantId=$tenantId&templateId=$id" 200
    Assert-Check ($page.Body -match 'value="LastPage" selected="selected"' -or $page.Body -match 'selected="selected" value="LastPage"') 'Last-page setting fills the form on reload'
    $job=Api POST "$WorkflowUrl/api/v1/tenants/$tenantId/translation-jobs" @{title='Last page footer pipeline';sourceLanguageCode='tr';targetLanguageCode='fa';notaryRequirement='NotRequired'} 201
    $collection="$DocumentsUrl/api/v1/tenants/$tenantId/translation-jobs/$($job.id)/documents"
    $document=Api POST $collection $null 201; $documentPath="$collection/$($document.id)"
    Api POST "$documentPath/template" @{templateId=$id;revisionNumber=8} 200 | Out-Null
    $pdf=Api POST "$documentPath/draft-revisions/1/pdfs" $null 201
    Assert-Check ($pdf.templateRevisionId -eq $last.revision.id -and $pdf.rendererVersion -match '^draft-html-v15-paragraph-spacing') 'PDF uses the exact last-page template revision and versioned renderer'
    $values['Form.ExpectedRevision']=8; $values['Form.FooterPlacement']='AllPages'
    Save-Template $values $page 302 | Out-Null
    $all=Api GET $path $null 200
    Assert-Check ($all.currentRevision -eq 9 -and -not (ConvertFrom-Json $all.revision.pageLayoutJson).PSObject.Properties['footerPlacement']) 'All-pages default remains legacy-compatible'
    $pinned=Api GET "$documentPath/template" $null 200
    Assert-Check ($pinned.id -eq $last.revision.id -and (ConvertFrom-Json $pinned.pageLayoutJson).footerPlacement -eq 'LastPage') 'An existing document retains its pinned footer policy'
    $again=Api POST "$documentPath/draft-revisions/1/pdfs" $null 200
    Assert-Check ($again.id -eq $pdf.id -and $again.sha256 -eq $pdf.sha256) 'Template edits do not overwrite the archived or cached PDF'
    $old=Api GET "$path/revisions/7" $null 200
    Assert-Check ($old.pageLayoutJson -eq $imageOnly.revision.pageLayoutJson) 'Old template layout bytes stay exact'
    $invalid=Template-Fields '' 0 "Invalid-footer-$runId"; $invalid['Form.FooterPlacement']='OddPages'
    $before=@(Api GET "$DocumentsUrl/api/v1/tenants/$tenantId/document-templates" $null 200).Count
    Save-Template $invalid $page 400 $logo | Out-Null
    Assert-Check (@(Api GET "$DocumentsUrl/api/v1/tenants/$tenantId/document-templates" $null 200).Count -eq $before) 'Unknown footer policies never create or upload a template'
    $qa=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../tmp/navigation-qa'))
    $semantic=(& $footerPythonTool -X utf8 (Join-Path $PSScriptRoot 'assert_footer_pdfs.py') $qa) -join "`n"
    Assert-Check ($LASTEXITCODE -eq 0) 'All per-page footer, header, page count and unsigned-notice checks pass'
    $semanticResult=ConvertFrom-Json $semantic
    $checks += $semanticResult.checks
    Write-Output "PASS: $($checks-$footerBaselineChecks) footer checks plus $footerBaselineChecks baseline/template checks. PDF files require separate visual inspection for overlap/clipping."
} finally { $client.Dispose(); $handler.Dispose() }
