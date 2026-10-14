#Requires -Version 7.0
param([string]$WebUrl='http://localhost:5067', [string]$IdentityUrl='http://localhost:5031',
    [string]$WorkflowUrl='http://localhost:5285', [string]$DocumentsUrl='http://localhost:5241',
    [string]$BillingUrl='http://localhost:5249')
$ErrorActionPreference='Stop'
foreach ($url in @($WebUrl,$IdentityUrl,$WorkflowUrl,$DocumentsUrl,$BillingUrl)) {
    $uri=[Uri]$url
    if (-not $uri.IsAbsoluteUri -or -not $uri.IsLoopback -or $uri.Scheme -ne 'http') { throw 'Table fixtures require loopback HTTP URLs only; never run on production.' }
}
. "$PSScriptRoot/Verify-WebWorkspace.ps1" -WebUrl $WebUrl -IdentityUrl $IdentityUrl -WorkflowUrl $WorkflowUrl -DocumentsUrl $DocumentsUrl -BillingUrl $BillingUrl
$tableBaselineChecks=$checks
$handler=[Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect=$false
$client=[Net.Http.HttpClient]::new($handler); $client.Timeout=[TimeSpan]::FromSeconds(45)
$tableEnvNames=@('ETRANSLATE_TABLE_BROWSER_TESTS','ETRANSLATE_TABLE_WEB_URL','ETRANSLATE_TABLE_TENANT','ETRANSLATE_TABLE_JOB','ETRANSLATE_TABLE_EMAIL','ETRANSLATE_TABLE_PASSWORD','PLAYWRIGHT_BROWSERS_PATH')
$tableOldEnv=@{}; foreach ($name in $tableEnvNames) { $tableOldEnv[$name]=[Environment]::GetEnvironmentVariable($name) }
function Table-Cell([string]$Type,[string]$Text,[string]$Direction='auto') {
    return @{type=$Type;attrs=@{colspan=1;rowspan=1;colwidth=$null;align=$null};content=@(@{type='paragraph';attrs=@{dir=$Direction};content=@(@{type='text';text=$Text})})}
}
try {
    $loginPage=Web GET '/Account/Login' 200
    Web POST '/Account/Login' 302 (Fields @{email=$email;password=$password;__RequestVerificationToken=(Csrf $loginPage.Body)}) | Out-Null
    $tableJob=Api POST "$WorkflowUrl/api/v1/tenants/$tenantId/translation-jobs" @{title='Synthetic table translation';sourceLanguageCode='fa';targetLanguageCode='tr';notaryRequirement='NotRequired'} 201
    $tableDocumentPath="$DocumentsUrl/api/v1/tenants/$tenantId/translation-jobs/$($tableJob.id)/documents"
    $tableDocument=Api POST $tableDocumentPath $null 201
    $tableApi="$tableDocumentPath/$($tableDocument.id)"
    Api POST "$tableApi/template" @{templateId=$templateId;revisionNumber=2} 200 | Out-Null
    $rows=[Collections.Generic.List[object]]::new()
    $rows.Add(@{type='tableRow';content=@((Table-Cell 'tableHeader' 'ردیف'),(Table-Cell 'tableHeader' 'مشخصات فارسی'),(Table-Cell 'tableHeader' 'Türkçe çeviri' 'ltr'))})
    for ($row=1;$row -le 40;$row++) {
        $rows.Add(@{type='tableRow';content=@((Table-Cell 'tableCell' "ROW-$row" 'ltr'),
            (Table-Cell 'tableCell' "نمونهٔ $row : نام و مشخصات ساختگی برای بررسی جدول ترجمه، پیوستگی حروف فارسی و چاپ چندصفحه‌ای؛ هیچ اطلاعات واقعی یا اعتبار حقوقی ندارد." 'rtl'),
            (Table-Cell 'tableCell' "Örnek $row : Türkçe çeviri, İ ı Ş ş Ç ç Ö ö Ü ü Ğ ğ. Bu tablo yalnızca test içindir; resmi belge veya elektronik imza değildir." 'ltr'))})
    }
    $tableJson=ConvertTo-Json @{type='doc';content=@(@{type='heading';attrs=@{level=2;dir='rtl'};content=@(@{type='text';text='جدول نمونهٔ ترجمه - بدون امضا'})},
        @{type='table';attrs=@{dir='rtl'};content=$rows.ToArray()},@{type='paragraph';content=@(@{type='text';text='پایان جدول نمونه.'})})} -Depth 20 -Compress
    $editorPath="/Workspace/Editor?tenantId=$tenantId&jobId=$($tableJob.id)"
    $page=Web GET $editorPath 200
    Web POST '/Workspace/SaveDraft' 302 (Fields @{tenantId=$tenantId;jobId=$tableJob.id;expectedRevision=1;mode='rich';editorContentJson=$tableJson;__RequestVerificationToken=(Csrf $page.Body)}) | Out-Null
    $saved=Api GET "$tableApi/draft-revisions/2" $null 200
    Assert-Check ($saved.editorContentJson -eq $tableJson -and $saved.plainText -match 'ROW-40') 'Rich table JSON is preserved exactly; text is only derived metadata'
    $page=Web GET $editorPath 200
    Assert-Check ($page.Body -match 'data-rich-compatible="true"' -and $page.Body -match 'data-command="insertTable"') 'Saved table opens rich editor with table controls'
    Api POST "$tableApi/draft-revisions" @{expectedCurrentRevision=1;editorContentJson=$tableJson} 409 | Out-Null
    $pdf=Api POST "$tableApi/draft-revisions/2/pdfs" $null 201
    Assert-Check ($pdf.kind -eq 'DraftUnsigned' -and $pdf.rendererVersion -match 'v8-tables') 'Table PDF remains unsigned and uses a new renderer identity'
    $cached=Api POST "$tableApi/draft-revisions/2/pdfs" $null 200
    Assert-Check ($cached.id -eq $pdf.id -and $cached.sha256 -eq $pdf.sha256) 'Repeated rendering reuses immutable bytes'
    $fileUrl="$tableApi/pdfs/$($pdf.id)"
    $download=Invoke-WebRequest $fileUrl -Headers $bearer -TimeoutSec 45
    [byte[]]$bytes=$download.Content
    Assert-Check ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() -eq $pdf.sha256) 'Table PDF download matches archived digest'
    $outputDirectory=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../output/pdf'))
    [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    $sample=Join-Path $outputDirectory "translation-table-$runId.pdf"
    [IO.File]::WriteAllBytes($sample,$bytes)
    # Browser tests operate only on the fresh synthetic account/job above.
    $env:ETRANSLATE_TABLE_BROWSER_TESTS='1'; $env:ETRANSLATE_TABLE_WEB_URL=$WebUrl
    $env:ETRANSLATE_TABLE_TENANT=$tenantId; $env:ETRANSLATE_TABLE_JOB=$tableJob.id
    $env:ETRANSLATE_TABLE_EMAIL=$email; $env:ETRANSLATE_TABLE_PASSWORD=$password
    $env:PLAYWRIGHT_BROWSERS_PATH=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../.local/pdf-browsers'))
    & dotnet test (Join-Path $PSScriptRoot '../ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-build --no-restore --filter 'FullyQualifiedName~TableEditorBrowserTests' --logger 'console;verbosity=normal'
    Assert-Check ($LASTEXITCODE -eq 0) 'Real local browser: table insertion/row/column/undo/save/reload/readonly tests pass'
    $latest=Api GET $tableDocumentPath $null 200
    Assert-Check ($latest.currentDraftRevision -eq 3) 'Browser save creates exactly one new revision'
    $historical=Api GET "$tableApi/draft-revisions/2" $null 200
    Assert-Check ($historical.editorContentJson -eq $tableJson) 'Browser editing never changes historical table JSON'
    $old=Invoke-WebRequest $fileUrl -Headers $bearer -TimeoutSec 45
    Assert-Check ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]]$old.Content)).ToLowerInvariant() -eq $pdf.sha256) 'Browser editing never rewrites archived PDF'
    # Unknown merged-cell JSON remains stored, but rich editing/rendering must reject it.
    $merged=$tableJson.Replace('"colspan":1','"colspan":2')
    Api POST "$tableApi/draft-revisions" @{expectedCurrentRevision=3;editorContentJson=$merged} 201 | Out-Null
    $page=Web GET $editorPath 200
    Assert-Check ($page.Body -match 'data-rich-compatible="false"' -and $page.Body -match 'name="editorContentJson"') 'Unsupported merged cells retain advanced JSON fallback'
    Api POST "$tableApi/draft-revisions/4/pdfs" $null 422 | Out-Null
    Assert-Check ((Api GET "$tableApi/draft-revisions/4" $null 200).editorContentJson -eq $merged) 'Unsupported export never flattens stored JSON'
    Assert-Check (@(Api GET "$tableApi/pdfs" $null 200).Count -eq 1) 'Rejected table export creates no PDF record'
    Api GET $fileUrl $null 401 @{} | Out-Null
    Api GET "$DocumentsUrl/api/v1/tenants/$($other.id)/translation-jobs/$($tableJob.id)/documents/$($tableDocument.id)/pdfs/$($pdf.id)" $null 404 | Out-Null
    # A structurally valid row that cannot fit a page must fail, not clip its text.
    $tallJson=ConvertTo-Json @{type='doc';content=@(@{type='table';content=@(@{type='tableRow';content=@((Table-Cell 'tableCell' ('متن بسیار بلند برای بررسی قطع نشدن محتوا. ' * 250) 'rtl'))})})} -Depth 20 -Compress
    Api POST "$tableApi/draft-revisions" @{expectedCurrentRevision=4;editorContentJson=$tallJson} 201 | Out-Null
    Api POST "$tableApi/draft-revisions/5/pdfs" $null 422 | Out-Null
    Assert-Check (@(Api GET "$tableApi/pdfs" $null 200).Count -eq 1) 'Oversized row creates no clipped PDF record'
    Write-Output "PASS: $($checks-$tableBaselineChecks) table checks plus $tableBaselineChecks baseline checks and one real-browser test. PDF sample: $sample"
} finally {
    foreach ($name in $tableEnvNames) { [Environment]::SetEnvironmentVariable($name,$tableOldEnv[$name]) }
    $client.Dispose(); $handler.Dispose()
}
