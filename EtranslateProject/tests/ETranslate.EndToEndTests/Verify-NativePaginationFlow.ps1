#Requires -Version 7.0
param(
    [string]$NodePath='C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe',
    [string]$PythonPath='C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$labRoot=Join-Path $projectRoot 'experiments/native-pagination-flow'
$output=Join-Path $projectRoot 'tmp/pdfs/native-pagination-flow'
foreach ($executable in @($NodePath,$PythonPath)) { if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Executable not found: $executable" } }
& $NodePath (Join-Path $labRoot 'build.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Native-flow lab build failed.' }
& $NodePath --test (Join-Path $labRoot 'fixtures.test.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Native-flow fixture checks failed.' }
$names=@('ETRANSLATE_NATIVE_FLOW_TESTS','ETRANSLATE_NATIVE_FLOW_ROOT','ETRANSLATE_NATIVE_FLOW_OUTPUT','ETRANSLATE_NATIVE_FLOW_NODE','PLAYWRIGHT_BROWSERS_PATH')
$previous=@{}; foreach ($name in $names) { $previous[$name]=[Environment]::GetEnvironmentVariable($name) }
try {
    $env:ETRANSLATE_NATIVE_FLOW_TESTS='1'; $env:ETRANSLATE_NATIVE_FLOW_ROOT=$labRoot; $env:ETRANSLATE_NATIVE_FLOW_OUTPUT=$output
    $env:ETRANSLATE_NATIVE_FLOW_NODE=[IO.Path]::GetFullPath($NodePath)
    $env:PLAYWRIGHT_BROWSERS_PATH=[IO.Path]::GetFullPath((Join-Path $projectRoot '../.local/pdf-browsers'))
    & dotnet test (Join-Path $PSScriptRoot '../ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-restore --filter 'FullyQualifiedName~NativePaginationFlowTests' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=native-flow.trx' --results-directory $output
    if ($LASTEXITCODE -ne 0) { throw 'Native-flow browser experiment failed; inspect TRX before retrying.' }
    & $PythonPath (Join-Path $labRoot 'compare_pdf.py') $output
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic PDF marker verification failed.' }
    Write-Output 'Experiment completed; inspect findings. Passing this harness is NOT production approval. No product API, SQL, real document or external service was used.'
} finally { foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name,$previous[$name]) } }
