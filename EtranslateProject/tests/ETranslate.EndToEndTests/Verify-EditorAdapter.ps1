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
    & $PythonPath (Join-Path $PSScriptRoot 'verify_paragraph_pdf.py') (Join-Path $projectRoot 'tmp/pdfs/paragraph')
    if ($LASTEXITCODE -ne 0) { throw 'Paragraph PDF alignment, line spacing or logical indentation differed.' }
    & $PythonPath (Join-Path $PSScriptRoot 'verify_inline_scripts_pdf.py') (Join-Path $projectRoot 'tmp/pdfs/inline-scripts')
    if ($LASTEXITCODE -ne 0) { throw 'Inline script baselines, sizes or colored strike differed.' }
    & $PythonPath (Join-Path $PSScriptRoot 'verify_paragraph_spacing_pdf.py') (Join-Path $projectRoot 'tmp/pdfs/paragraph-spacing')
    if ($LASTEXITCODE -ne 0) { throw 'Paragraph before/after spacing, first-line indent or marker pages differed.' }
    & $PythonPath (Join-Path $PSScriptRoot 'verify_table_insertion_pdf.py') (Join-Path $projectRoot 'tmp/pdfs/table-insertion')
    if ($LASTEXITCODE -ne 0) { throw 'Chosen table dimensions, header repetition or marker pages differed.' }
    & $PythonPath (Join-Path $PSScriptRoot 'verify_table_header_pdf.py') (Join-Path $projectRoot 'tmp/pdfs/table-header')
    if ($LASTEXITCODE -ne 0) { throw 'Existing table heading changes, repetition or marker pages differed.' }
    & $PythonPath (Join-Path $PSScriptRoot 'verify_table_widths_pdf.py') (Join-Path $projectRoot 'tmp/pdfs/table-widths')
    if ($LASTEXITCODE -ne 0) { throw 'Column percentages, direction or editor/PDF geometry differed.' }
    & $PythonPath (Join-Path $PSScriptRoot 'verify_cell_style_pdf.py') (Join-Path $projectRoot 'tmp/pdfs/cell-style')
    if ($LASTEXITCODE -ne 0) { throw 'Cell paint, borders, hidden edges or editor/PDF pages differed.' }
    & $PythonPath (Join-Path $PSScriptRoot 'verify_horizontal_merge_pdf.py') (Join-Path $projectRoot 'tmp/pdfs/horizontal-merge')
    if ($LASTEXITCODE -ne 0) { throw 'Horizontal spans, repeated headers or editor/PDF geometry differed.' }
    & $PythonPath (Join-Path $PSScriptRoot 'verify_body_images_pdf.py') (Join-Path $projectRoot 'tmp/pdfs/body-images')
    if ($LASTEXITCODE -ne 0) { throw 'Private image pages, dimensions, alignment or aspect differed.' }
    Write-Output 'Editor adapter and page-flow checks passed. Synthetic PDF comparison and temporary loopback host only; no SQL, AppHost or external services.'
} finally { foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name,$previous[$name]) } }
