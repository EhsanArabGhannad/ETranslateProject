#Requires -Version 7.0
param(
    [string]$SqlServer='.\ESIMSSQLSERVER',
    [string]$NodePath='C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe'
)
$ErrorActionPreference='Stop'
if ($SqlServer -notmatch '^\.(\\[A-Za-z0-9_]+)?$') { throw 'Only a local SQL instance is allowed.' }
$solution=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output=Join-Path $solution 'tmp/editor-uploads'; New-Item -ItemType Directory -Force $output | Out-Null
if (-not (Test-Path -LiteralPath $NodePath -PathType Leaf)) { throw "Executable missing: $NodePath" }
Push-Location (Join-Path $solution 'src/Web/ETranslate.Web')
try {
    & $NodePath --test 'ClientApp/tests/*.test.js'; if ($LASTEXITCODE -ne 0) { throw 'Client tests failed.' }
    & $NodePath ClientApp/build.mjs; if ($LASTEXITCODE -ne 0) { throw 'Client build failed.' }
} finally { Pop-Location }
& dotnet build (Join-Path $solution 'ETranslate.slnx') --no-restore -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
$names=@('ETRANSLATE_EDITOR_UPLOAD_TESTS','ETRANSLATE_EDITOR_UPLOAD_SERVER','ETRANSLATE_EDITOR_UPLOAD_ROOT',
    'ETRANSLATE_EDITOR_ADAPTER_TESTS','ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT','PLAYWRIGHT_BROWSERS_PATH')
$previous=@{}; foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
try {
    $env:PLAYWRIGHT_BROWSERS_PATH=[IO.Path]::GetFullPath((Join-Path $solution '../.local/pdf-browsers'))
    $env:ETRANSLATE_EDITOR_ADAPTER_TESTS='1'; $env:ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT=Join-Path $solution 'src/Web/ETranslate.Web'
    & dotnet test (Join-Path $solution 'tests/ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-build --no-restore --filter 'FullyQualifiedName~Upload_deadlines_keep|FullyQualifiedName~Confirmed_upload_conflicts|FullyQualifiedName~Acknowledged_source_upload|FullyQualifiedName~Private_image_upload_preserves|FullyQualifiedName~Production_host_preserves_edits_history_tables_and_save_retry_conflict_locks' --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) { throw 'Upload and editor browser checks failed.' }
    $env:ETRANSLATE_EDITOR_UPLOAD_TESTS='1'; $env:ETRANSLATE_EDITOR_UPLOAD_SERVER=$SqlServer; $env:ETRANSLATE_EDITOR_UPLOAD_ROOT=$solution
    $latest=Join-Path $output 'latest.json'; if (Test-Path -LiteralPath $latest) { Remove-Item -LiteralPath $latest }
    & dotnet test (Join-Path $solution 'tests/ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-build --no-restore --filter 'FullyQualifiedName~EditorUploadAcceptanceTests' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=editor-uploads.trx' --results-directory $output
    if ($LASTEXITCODE -ne 0) { throw 'Upload acceptance failed; inspect the run and cleanup report.' }
    $run=(Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json).runId
    if ($run -notmatch '^[a-f0-9]{32}$') { throw 'Invalid QA run identifier.' }
    $directory=Join-Path $solution "tmp/pdfs/editor-uploads/$run"
    $report=Get-Content (Join-Path $directory 'uploads-report.json') -Raw | ConvertFrom-Json
    $cleanup=Get-Content (Join-Path $directory 'cleanup.json') -Raw | ConvertFrom-Json
    if ($report.runId -ne $run -or $cleanup.runId -ne $run -or -not $cleanup.cleanupSucceeded -or $cleanup.databasesCreated -ne 6 -or $cleanup.existingDatabasesUsed) { throw 'QA isolation/cleanup failed.' }
    if (@($report.cases).Count -ne 12 -or @($report.errors).Count -or @($report.external).Count -or @($report.csp).Count -or @($report.cases | Where-Object { -not $_.passed }).Count) { throw 'Upload acceptance checks failed.' }
    Write-Output "PASS: 12 upload scenarios, exact metadata/blob counts and hashes, new QA databases removed. Report: $directory"
} finally { foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])} }
