#Requires -Version 7.0
param(
    [string]$SqlServer='.\ESIMSSQLSERVER',
    [string]$NodePath='C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe'
)
$ErrorActionPreference='Stop'
if ($SqlServer -notmatch '^\.(\\[A-Za-z0-9_]+)?$') { throw 'Only a local SQL instance is allowed.' }
$solution=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output=Join-Path $solution 'tmp/editor-access'
New-Item -ItemType Directory -Force $output | Out-Null
if (-not (Test-Path -LiteralPath $NodePath -PathType Leaf)) { throw "Executable missing: $NodePath" }
Push-Location (Join-Path $solution 'src/Web/ETranslate.Web')
try {
    & $NodePath ClientApp/build.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Client build failed.' }
} finally { Pop-Location }
& dotnet build (Join-Path $solution 'ETranslate.slnx') --no-restore -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
$names=@('ETRANSLATE_EDITOR_ACCESS_TESTS','ETRANSLATE_EDITOR_ACCESS_SERVER','ETRANSLATE_EDITOR_ACCESS_ROOT','PLAYWRIGHT_BROWSERS_PATH')
$previous=@{}; foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
try {
    $env:ETRANSLATE_EDITOR_ACCESS_TESTS='1'; $env:ETRANSLATE_EDITOR_ACCESS_SERVER=$SqlServer
    $env:ETRANSLATE_EDITOR_ACCESS_ROOT=$solution
    $env:PLAYWRIGHT_BROWSERS_PATH=[IO.Path]::GetFullPath((Join-Path $solution '../.local/pdf-browsers'))
    & dotnet test (Join-Path $solution 'tests/ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-build --no-restore --filter 'FullyQualifiedName~EditorAccessAcceptanceTests' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=editor-access.trx' --results-directory $output
    if ($LASTEXITCODE -ne 0) { throw 'Editor access acceptance failed; inspect the latest run and cleanup report.' }
    $run=(Get-Content (Join-Path $output 'latest.json') -Raw | ConvertFrom-Json).runId
    $directory=Join-Path $solution "tmp/pdfs/editor-access/$run"
    $report=Get-Content (Join-Path $directory 'access-report.json') -Raw | ConvertFrom-Json
    $cleanup=Get-Content (Join-Path $directory 'cleanup.json') -Raw | ConvertFrom-Json
    if (-not $cleanup.cleanupSucceeded -or $cleanup.databasesCreated -ne 6 -or $cleanup.existingDatabasesUsed) { throw 'QA isolation/cleanup failed.' }
    if (@($report.errors).Count -or @($report.external).Count -or @($report.csp).Count -or @($report.scenarios | Where-Object { -not $_.passed }).Count) { throw 'Browser or access checks failed.' }
    Write-Output "PASS: $(@($report.scenarios).Count) editor access scenarios; new databases removed. Report: $directory"
} finally { foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])} }
