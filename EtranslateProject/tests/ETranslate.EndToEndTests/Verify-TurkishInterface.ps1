#Requires -Version 7.0
param([string]$WebUrl='http://localhost:5067', [string]$IdentityUrl='http://localhost:5031',
    [string]$WorkflowUrl='http://localhost:5285', [string]$DocumentsUrl='http://localhost:5241', [string]$BillingUrl='http://localhost:5249')
$ErrorActionPreference='Stop'
foreach ($url in @($WebUrl,$IdentityUrl,$WorkflowUrl,$DocumentsUrl,$BillingUrl)) {
    $uri=[Uri]$url
    if (-not $uri.IsAbsoluteUri -or -not $uri.IsLoopback -or $uri.Scheme -ne 'http') { throw 'Only local HTTP synthetic fixtures are allowed.' }
}
. "$PSScriptRoot/Verify-WebWorkspace.ps1" -WebUrl $WebUrl -IdentityUrl $IdentityUrl -WorkflowUrl $WorkflowUrl -DocumentsUrl $DocumentsUrl -BillingUrl $BillingUrl
$trBaseline=$checks
$handler=[Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect=$false
$client=[Net.Http.HttpClient]::new($handler); $client.Timeout=[TimeSpan]::FromSeconds(40)
$trEnv=@('ETRANSLATE_TR_BROWSER_TESTS','ETRANSLATE_NAV_WEB_URL','ETRANSLATE_NAV_TENANT','ETRANSLATE_TR_JOB','ETRANSLATE_NAV_EMAIL','ETRANSLATE_NAV_PASSWORD','ETRANSLATE_NAV_QA_PATH','PLAYWRIGHT_BROWSERS_PATH')
$trOldEnv=@{}; foreach ($name in $trEnv) { $trOldEnv[$name]=[Environment]::GetEnvironmentVariable($name) }
try {
    $before=Api GET "$documentPath/$($document.id)/draft-revisions/2" $null 200
    $page=Web GET '/Account/Login' 200
    Web POST '/Account/Login' 302 (Fields @{email=$email;password=$password;__RequestVerificationToken=(Csrf $page.Body)}) | Out-Null
    $page=Web GET "/Settings/Templates?tenantId=$tenantId" 200
    $invalid=Web POST '/Settings/SaveTemplate' 400 (Fields @{'Form.TenantId'=$tenantId;'Form.Name'='';'Form.MarginTop'='2,5';__RequestVerificationToken=(Csrf $page.Body)})
    $decoded=[Net.WebUtility]::HtmlDecode($invalid.Body)
    Assert-Check ($decoded -match 'Şablon adı zorunludur' -and $decoded -match 'ondalık ayırıcı') 'Server validation is Turkish and comma decimals are rejected, never silently reinterpreted'
    Assert-Check ($page.Body -match '<html lang="tr" dir="ltr">') 'Interface language and direction are explicit'
    $env:ETRANSLATE_TR_BROWSER_TESTS='1'; $env:ETRANSLATE_NAV_WEB_URL=$WebUrl; $env:ETRANSLATE_NAV_TENANT=$tenantId
    $env:ETRANSLATE_TR_JOB=$jobId; $env:ETRANSLATE_NAV_EMAIL=$email; $env:ETRANSLATE_NAV_PASSWORD=$password
    $env:ETRANSLATE_NAV_QA_PATH=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../tmp/turkish-interface-qa'))
    $env:PLAYWRIGHT_BROWSERS_PATH=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../.local/pdf-browsers'))
    & dotnet test (Join-Path $PSScriptRoot '../ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-build --no-restore --filter 'FullyQualifiedName~TurkishInterfaceBrowserTests' --logger 'console;verbosity=normal'
    Assert-Check ($LASTEXITCODE -eq 0) 'Turkish UI, mixed-language content, decimal margins, routes and mobile browser checks pass'
    $after=Api GET "$documentPath/$($document.id)/draft-revisions/2" $null 200
    Assert-Check ($before.editorContentJson -ceq $after.editorContentJson -and $before.templateRevisionId -eq $after.templateRevisionId) 'Historical document content and template snapshot are unchanged'
    $templates=@(Api GET "$DocumentsUrl/api/v1/tenants/$tenantId/document-templates" $null 200)
    $created=$templates | Where-Object name -eq 'Türkçe / قالب آزمایشی'
    Assert-Check ($null -ne $created) 'Browser-created multilingual template exists'
    $detail=Api GET "$DocumentsUrl/api/v1/tenants/$tenantId/document-templates/$($created.id)" $null 200
    $layout=ConvertFrom-Json $detail.revision.pageLayoutJson
    Assert-Check ($layout.marginsMm.top -eq 25.5 -and $layout.marginsMm.right -eq 15.5 -and $layout.footerPlacement -eq 'LastPage') 'Turkish-interface fractional margin values are persisted exactly in the unchanged API layout contract'
    Write-Output "PASS: $($checks-$trBaseline) Turkish-interface checks plus $trBaseline baseline checks and one real-browser test. Development tenant: $tenantId"
} finally {
    foreach ($name in $trEnv) { [Environment]::SetEnvironmentVariable($name,$trOldEnv[$name]) }
    $client.Dispose(); $handler.Dispose()
}
