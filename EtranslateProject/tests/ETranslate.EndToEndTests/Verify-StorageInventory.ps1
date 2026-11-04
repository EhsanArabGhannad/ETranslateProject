#Requires -Version 7.0
param([string]$SqlServer='.\ESIMSSQLSERVER')
$ErrorActionPreference='Stop'
if ($SqlServer -notmatch '^\.(\\[A-Za-z0-9_]+)?$') { throw 'Only a local SQL instance is allowed.' }
$solution=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output=Join-Path $solution 'tmp/storage-inventory'; New-Item -ItemType Directory -Force $output | Out-Null
& dotnet build (Join-Path $solution 'ETranslate.slnx') --no-restore -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Solution build failed; restore existing dependencies first if needed.' }
$names=@('ETRANSLATE_STORAGE_INVENTORY_TESTS','ETRANSLATE_STORAGE_INVENTORY_SERVER','ETRANSLATE_STORAGE_INVENTORY_ROOT','ETRANSLATE_STORAGE_INVENTORY_PWSH')
$previous=@{}; foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
try {
    $env:ETRANSLATE_STORAGE_INVENTORY_TESTS='1'; $env:ETRANSLATE_STORAGE_INVENTORY_SERVER=$SqlServer
    $env:ETRANSLATE_STORAGE_INVENTORY_ROOT=$solution; $env:ETRANSLATE_STORAGE_INVENTORY_PWSH=(Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
    $latest=Join-Path $output 'latest.json'; if(Test-Path -LiteralPath $latest){Remove-Item -LiteralPath $latest}
    & dotnet test (Join-Path $solution 'tests/ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-build --no-restore --filter 'FullyQualifiedName~StorageInventory' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=storage-inventory.trx' --results-directory $output
    if($LASTEXITCODE -ne 0){throw 'Inventory acceptance failed; inspect run/cleanup reports.'}
    $run=(Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json).runId
    if($run -notmatch '^[a-f0-9]{32}$'){throw 'Invalid QA run identifier.'}
    $directory=Join-Path $solution "tmp/pdfs/storage-inventory/$run"
    $acceptance=Get-Content -LiteralPath (Join-Path $directory 'acceptance.json') -Raw | ConvertFrom-Json
    $cleanup=Get-Content -LiteralPath (Join-Path $directory 'cleanup.json') -Raw | ConvertFrom-Json
    if($acceptance.runId -ne $run -or $cleanup.runId -ne $run -or $acceptance.existingDatabasesUsed -or -not $acceptance.syntheticDataOnly -or @($acceptance.checks).Count -ne 8 -or -not $cleanup.cleanupSucceeded -or $cleanup.databasesCreated -ne 6 -or $cleanup.existingDatabasesUsed){throw 'Acceptance/isolation/cleanup checks failed.'}
    $report=(Get-Content -LiteralPath (Join-Path $directory 'inventory.json') -Raw | ConvertFrom-Json).report
    if(-not $report.complete -or -not $report.readOnly -or $report.authorizesDeletion -or @($report.entries).Count -ne 12 -or $report.counts.Verified -ne 5){throw 'Inventory output failed verification.'}
    Write-Output "PASS: inventory checks, SELECT-only SQL read, real console report, Windows junction exclusion and unchanged bytes/metadata. Six fresh QA databases removed. Report: $directory"
} finally {foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])}}
