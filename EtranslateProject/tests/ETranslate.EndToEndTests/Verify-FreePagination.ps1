#Requires -Version 7.0
param(
    [string]$NodePath='C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe',
    [string]$PythonPath='C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$labRoot=Join-Path $projectRoot 'experiments/tiptap-pagination-free'
$output=Join-Path $projectRoot 'tmp/free-pagination'
foreach ($executable in @($NodePath,$PythonPath)) { if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Executable not found: $executable" } }
& $NodePath (Join-Path $labRoot 'build.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Free pagination lab build failed.' }
& $NodePath --test (Join-Path $labRoot 'fixtures.test.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Free pagination fixture checks failed.' }
$names=@('ETRANSLATE_FREE_PAGINATION_TESTS','ETRANSLATE_FREE_PAGINATION_ROOT','ETRANSLATE_FREE_PAGINATION_OUTPUT','ETRANSLATE_FREE_PAGINATION_NODE','PLAYWRIGHT_BROWSERS_PATH')
$previous=@{}; foreach ($name in $names) { $previous[$name]=[Environment]::GetEnvironmentVariable($name) }
try {
    $env:ETRANSLATE_FREE_PAGINATION_TESTS='1'; $env:ETRANSLATE_FREE_PAGINATION_ROOT=$labRoot
    $env:ETRANSLATE_FREE_PAGINATION_OUTPUT=$output; $env:ETRANSLATE_FREE_PAGINATION_NODE=[IO.Path]::GetFullPath($NodePath)
    $env:PLAYWRIGHT_BROWSERS_PATH=[IO.Path]::GetFullPath((Join-Path $projectRoot '../.local/pdf-browsers'))
    & dotnet test (Join-Path $PSScriptRoot '../ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-restore --filter 'FullyQualifiedName~FreePaginationLabTests' --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) { throw 'Free pagination browser experiment failed.' }
    & $PythonPath (Join-Path $labRoot 'compare_pdf.py') $output
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic PDF text comparison failed.' }
    Write-Output 'Experiment completed. Inspect layout findings; passing the harness is NOT production approval.'
} finally { foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name,$previous[$name]) } }
