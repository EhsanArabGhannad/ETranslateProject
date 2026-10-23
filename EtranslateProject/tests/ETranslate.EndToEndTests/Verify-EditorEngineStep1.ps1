#Requires -Version 7.0
param([string]$NodePath='C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe')
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$labRoot=Join-Path $projectRoot 'experiments/editor-engine-step1'
if (-not (Test-Path -LiteralPath $NodePath -PathType Leaf)) { throw 'Pass the installed Node executable using -NodePath.' }
& $NodePath (Join-Path $labRoot 'build.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Isolated editor lab build failed.' }
& $NodePath --test (Join-Path $labRoot 'fixtures.test.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Synthetic fixture tests failed.' }
$names=@('ETRANSLATE_EDITOR_SPIKE_TESTS','ETRANSLATE_EDITOR_SPIKE_ROOT','ETRANSLATE_EDITOR_SPIKE_OUTPUT','ETRANSLATE_EDITOR_SPIKE_NODE','PLAYWRIGHT_BROWSERS_PATH')
$previous=@{}; foreach ($name in $names) { $previous[$name]=[Environment]::GetEnvironmentVariable($name) }
try {
    $env:ETRANSLATE_EDITOR_SPIKE_TESTS='1'; $env:ETRANSLATE_EDITOR_SPIKE_ROOT=$labRoot
    $env:ETRANSLATE_EDITOR_SPIKE_OUTPUT=Join-Path $projectRoot 'tmp/editor-engine-step1'
    $env:ETRANSLATE_EDITOR_SPIKE_NODE=[IO.Path]::GetFullPath($NodePath)
    $env:PLAYWRIGHT_BROWSERS_PATH=[IO.Path]::GetFullPath((Join-Path $projectRoot '../.local/pdf-browsers'))
    & dotnet test (Join-Path $PSScriptRoot '../ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-build --no-restore --filter 'FullyQualifiedName~EditorEngineSpikeTests' --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) { throw 'Editor-engine baseline browser/PDF experiment failed.' }
    Write-Output 'PASS: isolated synthetic HTML, schema, bounded page measurement and production-renderer reference checks. No production endpoints or documents used.'
} finally { foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name,$previous[$name]) } }
