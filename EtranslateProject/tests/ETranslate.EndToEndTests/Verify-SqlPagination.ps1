#Requires -Version 7.0
param(
    [string]$SqlServer='.\ESIMSSQLSERVER',
    [string]$NodePath='C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe',
    [string]$PythonPath='C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
)
$ErrorActionPreference='Stop'
if ($SqlServer -notmatch '^\.(\\[A-Za-z0-9_]+)?$') { throw 'Only a local SQL instance is allowed.' }
$solution=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output=Join-Path $solution 'tmp/sql-pagination'
New-Item -ItemType Directory -Force $output | Out-Null
foreach ($file in @($NodePath,$PythonPath)) { if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Executable missing: $file" } }
Push-Location (Join-Path $solution 'src/Web/ETranslate.Web')
try {
    & $NodePath ClientApp/build.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Client build failed.' }
} finally { Pop-Location }
& dotnet build (Join-Path $solution 'ETranslate.slnx') --no-restore -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
$names=@('ETRANSLATE_SQL_PAGINATION_TESTS','ETRANSLATE_SQL_PAGINATION_SERVER','ETRANSLATE_SQL_PAGINATION_ROOT','PLAYWRIGHT_BROWSERS_PATH')
$previous=@{}; foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
try {
    $env:ETRANSLATE_SQL_PAGINATION_TESTS='1'; $env:ETRANSLATE_SQL_PAGINATION_SERVER=$SqlServer
    $env:ETRANSLATE_SQL_PAGINATION_ROOT=$solution
    $env:PLAYWRIGHT_BROWSERS_PATH=[IO.Path]::GetFullPath((Join-Path $solution '../.local/pdf-browsers'))
    & dotnet test (Join-Path $solution 'tests/ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-build --no-restore --filter 'FullyQualifiedName~SqlPaginationAcceptanceTests' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=sql-pagination.trx' --results-directory $output
    if ($LASTEXITCODE -ne 0) { throw 'SQL-backed acceptance failed; inspect the run logs and cleanup report.' }
    $run=(Get-Content (Join-Path $output 'latest.json') -Raw | ConvertFrom-Json).runId
    & $PythonPath (Join-Path $PSScriptRoot 'verify_sql_pagination.py') (Join-Path $solution "tmp/pdfs/sql-pagination/$run")
    if ($LASTEXITCODE -ne 0) { throw 'Persisted product PDF verification failed.' }
    Write-Output "PASS: real product services, isolated SQL databases, browser save/reload and archived PDF checks. Report: tmp/pdfs/sql-pagination/$run"
} finally { foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])} }
