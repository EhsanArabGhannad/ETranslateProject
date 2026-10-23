#Requires -Version 7.0
param(
    [string]$NodePath='C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe',
    [string]$PythonPath='C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$labRoot=Join-Path $projectRoot 'experiments/canvas-editor-trial'
& $NodePath (Join-Path $labRoot 'build.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Canvas trial build failed.' }
& $NodePath --test (Join-Path $labRoot 'fixtures.test.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Canvas fixtures failed.' }
$names=@('ETRANSLATE_CANVAS_TRIAL_TESTS','ETRANSLATE_CANVAS_TRIAL_ROOT','ETRANSLATE_CANVAS_TRIAL_OUTPUT','ETRANSLATE_CANVAS_TRIAL_NODE','PLAYWRIGHT_BROWSERS_PATH')
$previous=@{};foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
try {
    $env:ETRANSLATE_CANVAS_TRIAL_TESTS='1';$env:ETRANSLATE_CANVAS_TRIAL_ROOT=$labRoot
    $env:ETRANSLATE_CANVAS_TRIAL_OUTPUT=Join-Path $projectRoot 'tmp/canvas-editor-trial';$env:ETRANSLATE_CANVAS_TRIAL_NODE=$NodePath
    $env:PLAYWRIGHT_BROWSERS_PATH=[IO.Path]::GetFullPath((Join-Path $projectRoot '../.local/pdf-browsers'))
    & dotnet test (Join-Path $PSScriptRoot '../ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-restore --filter 'FullyQualifiedName~CanvasEditorTrialTests' --logger 'console;verbosity=normal'
    if($LASTEXITCODE -ne 0){throw 'Canvas browser trial failed; older reports are not current results.'}
    & $PythonPath (Join-Path $labRoot 'compare_exports.py') $env:ETRANSLATE_CANVAS_TRIAL_OUTPUT
    if($LASTEXITCODE -ne 0){throw 'Canvas export comparison failed.'}
    Write-Output 'Trial measured. Read the findings; this is not production approval.'
} finally {foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])}}
