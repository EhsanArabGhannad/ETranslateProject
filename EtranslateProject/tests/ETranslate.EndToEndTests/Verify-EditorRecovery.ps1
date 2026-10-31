#Requires -Version 7.0
param(
    [string]$SqlServer='.\ESIMSSQLSERVER',
    [string]$NodePath='C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe'
)
$ErrorActionPreference='Stop'
if ($SqlServer -notmatch '^\.(\\[A-Za-z0-9_]+)?$') { throw 'Only a local SQL instance is allowed.' }
$solution=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output=Join-Path $solution 'tmp/editor-recovery'
New-Item -ItemType Directory -Force $output | Out-Null
if (-not (Test-Path -LiteralPath $NodePath -PathType Leaf)) { throw "Executable missing: $NodePath" }
Push-Location (Join-Path $solution 'src/Web/ETranslate.Web')
try { & $NodePath ClientApp/build.mjs; if ($LASTEXITCODE -ne 0) { throw 'Client build failed.' } } finally { Pop-Location }
& dotnet build (Join-Path $solution 'ETranslate.slnx') --no-restore -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
$names=@('ETRANSLATE_EDITOR_RECOVERY_TESTS','ETRANSLATE_EDITOR_RECOVERY_SERVER','ETRANSLATE_EDITOR_RECOVERY_ROOT','PLAYWRIGHT_BROWSERS_PATH')
$previous=@{}; foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
try {
    $env:ETRANSLATE_EDITOR_RECOVERY_TESTS='1'; $env:ETRANSLATE_EDITOR_RECOVERY_SERVER=$SqlServer
    $env:ETRANSLATE_EDITOR_RECOVERY_ROOT=$solution
    $env:PLAYWRIGHT_BROWSERS_PATH=[IO.Path]::GetFullPath((Join-Path $solution '../.local/pdf-browsers'))
    $latest=Join-Path $output 'latest.json'; if (Test-Path -LiteralPath $latest) { Remove-Item -LiteralPath $latest }
    & dotnet test (Join-Path $solution 'tests/ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-build --no-restore --filter 'FullyQualifiedName~EditorRecoveryAcceptanceTests' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=editor-recovery.trx' --results-directory $output
    if ($LASTEXITCODE -ne 0) { throw 'Editor recovery acceptance failed; inspect the run and cleanup report.' }
    $run=(Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json).runId
    $directory=Join-Path $solution "tmp/pdfs/editor-recovery/$run"
    $report=Get-Content (Join-Path $directory 'recovery-report.json') -Raw | ConvertFrom-Json
    $cleanup=Get-Content (Join-Path $directory 'cleanup.json') -Raw | ConvertFrom-Json
    if ($report.runId -ne $run -or $cleanup.runId -ne $run -or -not $cleanup.cleanupSucceeded -or $cleanup.databasesCreated -ne 6 -or $cleanup.existingDatabasesUsed) { throw 'QA isolation/cleanup failed.' }
    if (@($report.cases).Count -ne 6 -or @($report.errors).Count -or @($report.external).Count -or @($report.csp).Count -or @($report.cases | Where-Object { -not $_.passed }).Count) { throw 'Browser or recovery checks failed.' }
    Write-Output "PASS: 6 recovery scenarios; only owned services restarted and new databases removed. Report: $directory"
} finally { foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])} }
