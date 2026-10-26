#Requires -Version 7.0
param(
    [string]$NodePath='C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe',
    [string]$PythonPath='C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$webRoot=Join-Path $projectRoot 'src/Web/ETranslate.Web'
$browserResults=Join-Path $projectRoot 'tmp/editor-adapter'
if (-not (Test-Path -LiteralPath $NodePath -PathType Leaf)) { throw "Executable not found: $NodePath" }
if (-not (Test-Path -LiteralPath $PythonPath -PathType Leaf)) { throw "Executable not found: $PythonPath" }
Push-Location $webRoot
try {
    & $NodePath --test 'ClientApp/tests/*.test.js'
    if ($LASTEXITCODE -ne 0) { throw 'Client contract tests failed.' }
    & $NodePath 'ClientApp/build.mjs'
    if ($LASTEXITCODE -ne 0) { throw 'Production client build failed.' }
} finally { Pop-Location }
$names=@('ETRANSLATE_EDITOR_ADAPTER_TESTS','ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT','PLAYWRIGHT_BROWSERS_PATH')
$previous=@{}; foreach ($name in $names) { $previous[$name]=[Environment]::GetEnvironmentVariable($name) }
try {
    $env:ETRANSLATE_EDITOR_ADAPTER_TESTS='1'; $env:ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT=$webRoot
    $env:PLAYWRIGHT_BROWSERS_PATH=[IO.Path]::GetFullPath((Join-Path $projectRoot '../.local/pdf-browsers'))
    & dotnet test (Join-Path $PSScriptRoot '../ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-restore --filter 'FullyQualifiedName~EditorAdapterBrowserTests' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=editor-adapter.trx' --results-directory $browserResults
    if ($LASTEXITCODE -ne 0) { throw 'Editor adapter browser tests failed.' }
    & $PythonPath (Join-Path $PSScriptRoot 'verify_product_pagination.py') $browserResults
    if ($LASTEXITCODE -ne 0) { throw 'Product editor/PDF marker pages differed.' }
    & $PythonPath (Join-Path $PSScriptRoot 'verify_typography_pdf.py') (Join-Path $projectRoot 'tmp/pdfs/typography/selected-text.pdf')
    if ($LASTEXITCODE -ne 0) { throw 'Typography PDF font, size, color or highlight differed.' }
    Write-Output 'Editor adapter and page-flow checks passed. Synthetic PDF comparison and temporary loopback host only; no SQL, AppHost or external services.'
} finally { foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name,$previous[$name]) } }
