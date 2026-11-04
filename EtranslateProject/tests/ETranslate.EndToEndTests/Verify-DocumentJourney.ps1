#Requires -Version 7.0
param([string]$SqlServer='.\ESIMSSQLSERVER')
$ErrorActionPreference='Stop'
if($SqlServer -notmatch '^\.(\\[A-Za-z0-9_]+)?$'){throw 'Only a local SQL instance is allowed.'}
$solution=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output=Join-Path $solution 'tmp/document-journey';New-Item -ItemType Directory -Force $output | Out-Null
& dotnet build (Join-Path $solution 'ETranslate.slnx') --no-restore -v quiet
if($LASTEXITCODE -ne 0){throw 'Build failed.'}
$names=@('ETRANSLATE_DOCUMENT_JOURNEY_TESTS','ETRANSLATE_DOCUMENT_JOURNEY_ROOT','ETRANSLATE_DOCUMENT_JOURNEY_SERVER','PLAYWRIGHT_BROWSERS_PATH')
$previous=@{};foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
try{
    $env:ETRANSLATE_DOCUMENT_JOURNEY_TESTS='1';$env:ETRANSLATE_DOCUMENT_JOURNEY_ROOT=$solution;$env:ETRANSLATE_DOCUMENT_JOURNEY_SERVER=$SqlServer
    $env:PLAYWRIGHT_BROWSERS_PATH=[IO.Path]::GetFullPath((Join-Path $solution '../.local/pdf-browsers'))
    $latest=Join-Path $output 'latest.json';if(Test-Path -LiteralPath $latest){Remove-Item -LiteralPath $latest}
    & dotnet test (Join-Path $solution 'tests/ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-build --no-restore --filter 'FullyQualifiedName~DocumentJourneyAcceptanceTests' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=document-journey.trx' --results-directory $output
    if($LASTEXITCODE -ne 0){throw 'Journey acceptance failed; inspect run/cleanup reports.'}
    $run=(Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json).runId
    if($run -notmatch '^[a-f0-9]{32}$'){throw 'Invalid QA identifier.'}
    $directory=Join-Path $solution "tmp/pdfs/document-journey/$run"
    $report=Get-Content -LiteralPath (Join-Path $directory 'journey-report.json') -Raw | ConvertFrom-Json
    $cleanup=Get-Content -LiteralPath (Join-Path $directory 'cleanup.json') -Raw | ConvertFrom-Json
    if($report.runId -ne $run -or $cleanup.runId -ne $run -or @($report.cases).Count -ne 2 -or @($report.cases | Where-Object {-not $_.passed}).Count -or @($report.errors).Count -or @($report.external).Count -or @($report.csp).Count -or $report.existingDatabasesUsed -or -not $cleanup.cleanupSucceeded -or $cleanup.databasesCreated -ne 6 -or $cleanup.existingDatabasesUsed){throw 'Journey/isolation/cleanup checks failed.'}
    Write-Output "PASS: office and independent journeys, real six services, edit/approval/unsigned preview/reopen/cancel, six QA catalogs removed. Report: $directory"
}finally{foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])}}
