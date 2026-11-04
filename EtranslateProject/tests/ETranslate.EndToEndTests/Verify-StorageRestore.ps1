#Requires -Version 7.0
param([string]$SqlServer='.\ESIMSSQLSERVER')
$ErrorActionPreference='Stop'
if($SqlServer -notmatch '^\.(\\[A-Za-z0-9_]+)?$'){throw 'Only a local SQL instance is allowed.'}
$solution=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output=Join-Path $solution 'tmp/storage-restore'; New-Item -ItemType Directory -Force $output | Out-Null
& dotnet build (Join-Path $solution 'ETranslate.slnx') --no-restore -v quiet
if($LASTEXITCODE -ne 0){throw 'Build failed.'}
$names=@('ETRANSLATE_STORAGE_RESTORE_TESTS','ETRANSLATE_STORAGE_RESTORE_ROOT','ETRANSLATE_STORAGE_RESTORE_SERVER')
$previous=@{}; foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
try {
    $env:ETRANSLATE_STORAGE_RESTORE_TESTS='1'; $env:ETRANSLATE_STORAGE_RESTORE_ROOT=$solution; $env:ETRANSLATE_STORAGE_RESTORE_SERVER=$SqlServer
    $latest=Join-Path $output 'latest.json'; if(Test-Path -LiteralPath $latest){Remove-Item -LiteralPath $latest}
    & dotnet test (Join-Path $solution 'tests/ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-build --no-restore --filter 'FullyQualifiedName~StorageRestoreSqlTests' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=storage-restore.trx' --results-directory $output
    if($LASTEXITCODE -ne 0){throw 'Restore rehearsal failed; inspect checks/cleanup in the owned output.'}
    $run=(Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json).runId
    if($run -notmatch '^[a-f0-9]{32}$'){throw 'Invalid QA identifier.'}
    $directory=Join-Path $solution "tmp/pdfs/storage-restore/$run"
    $report=Get-Content -LiteralPath (Join-Path $directory 'restore-report.json') -Raw | ConvertFrom-Json
    $cleanup=Get-Content -LiteralPath (Join-Path $directory 'cleanup.json') -Raw | ConvertFrom-Json
    if($report.runId -ne $run -or $cleanup.runId -ne $run -or @($report.checks).Count -ne 8 -or $report.existingDatabasesUsed -or -not $report.syntheticOnly -or -not $report.sourceWritersStopped -or $report.assetCount -ne 5 -or -not $cleanup.cleanupSucceeded -or $cleanup.existingDatabasesUsed -or $cleanup.databasesCreated -ne 7){throw 'Rehearsal/isolation/cleanup verification failed.'}
    Write-Output "PASS: 8 restore checks; real SQL backup/restore and five managed artifact kinds; seven owned QA catalogs removed. Report: $directory"
} finally {foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])}}
