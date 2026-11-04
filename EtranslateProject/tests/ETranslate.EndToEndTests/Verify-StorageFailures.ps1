#Requires -Version 7.0
param([string]$SqlServer='.\ESIMSSQLSERVER')
$ErrorActionPreference='Stop'
if ($SqlServer -notmatch '^\.(\\[A-Za-z0-9_]+)?$') { throw 'Only a local SQL instance is allowed.' }
$solution=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output=Join-Path $solution 'tmp/storage-failures'
New-Item -ItemType Directory -Force $output | Out-Null
& dotnet build (Join-Path $solution 'ETranslate.slnx') --no-restore -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
$names=@('ETRANSLATE_STORAGE_FAILURE_TESTS','ETRANSLATE_STORAGE_FAILURE_SERVER','ETRANSLATE_STORAGE_FAILURE_ROOT')
$previous=@{}; foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
try {
    $env:ETRANSLATE_STORAGE_FAILURE_TESTS='1'
    $env:ETRANSLATE_STORAGE_FAILURE_SERVER=$SqlServer
    $env:ETRANSLATE_STORAGE_FAILURE_ROOT=$solution
    $latest=Join-Path $output 'latest.json'
    if (Test-Path -LiteralPath $latest) { Remove-Item -LiteralPath $latest }
    & dotnet test (Join-Path $solution 'tests/ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-build --no-restore --filter 'FullyQualifiedName~DocumentStorageFailureAcceptanceTests|FullyQualifiedName~PendingBlobTests' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=storage-failures.trx' --results-directory $output
    if ($LASTEXITCODE -ne 0) { throw 'Storage acceptance failed; inspect the run and cleanup report.' }
    $run=(Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json).runId
    if ($run -notmatch '^[a-f0-9]{32}$') { throw 'Invalid QA run identifier.' }
    $directory=Join-Path $solution "tmp/pdfs/storage-failures/$run"
    $report=Get-Content -LiteralPath (Join-Path $directory 'storage-report.json') -Raw | ConvertFrom-Json
    $cleanup=Get-Content -LiteralPath (Join-Path $directory 'cleanup.json') -Raw | ConvertFrom-Json
    if ($report.runId -ne $run -or $cleanup.runId -ne $run -or -not $cleanup.cleanupSucceeded -or $cleanup.databasesCreated -ne 6 -or $cleanup.existingDatabasesUsed -or $report.existingDatabasesUsed) { throw 'QA isolation/cleanup failed.' }
    if (@($report.cases).Count -ne 32 -or @($report.cases | Where-Object { -not $_.passed }).Count -or -not $report.sqlAndFilesystemAreReal -or -not $report.dependenciesAndRenderingAreTestDoubles) { throw 'Storage acceptance checks failed.' }
    Write-Output "PASS: 32 endpoint scenarios and 8 cleanup checks; real isolated SQL/files, synthetic dependencies/rendering, six QA databases removed. Report: $directory"
} finally { foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])} }
