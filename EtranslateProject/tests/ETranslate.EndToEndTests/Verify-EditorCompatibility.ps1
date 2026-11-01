#Requires -Version 7.0
param(
    [string]$SqlServer='.\ESIMSSQLSERVER',
    [string]$NodePath='C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe',
    [string]$PythonPath='C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe',
    [switch]$BrowserOnly
)
$ErrorActionPreference='Stop'
if ($SqlServer -notmatch '^\.(\\[A-Za-z0-9_]+)?$') { throw 'Only a local SQL instance is allowed.' }
$solution=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$webRoot=Join-Path $solution 'src/Web/ETranslate.Web'
$output=Join-Path $solution 'tmp/editor-compatibility'
$matrix=Join-Path $output ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $matrix | Out-Null
foreach ($executable in @($NodePath,$PythonPath)) { if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Executable missing: $executable" } }
# Browser channels must already be installed. No automatic package/browser download or user-profile reuse.
Push-Location $webRoot
try {
    & $NodePath --test 'ClientApp/tests/*.test.js'; if ($LASTEXITCODE -ne 0) { throw 'Client tests failed.' }
    & $NodePath ClientApp/build.mjs; if ($LASTEXITCODE -ne 0) { throw 'Client build failed.' }
} finally { Pop-Location }
& dotnet build (Join-Path $solution 'ETranslate.slnx') --no-restore -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
$names=@('ETRANSLATE_EDITOR_COMPATIBILITY_TESTS','ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT','ETRANSLATE_EDITOR_COMPATIBILITY_OUTPUT',
    'ETRANSLATE_EDITOR_COMPATIBILITY_SQL_TESTS','ETRANSLATE_EDITOR_COMPATIBILITY_SERVER','ETRANSLATE_EDITOR_COMPATIBILITY_ROOT','PLAYWRIGHT_BROWSERS_PATH')
$previous=@{}; foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
try {
    $env:ETRANSLATE_EDITOR_COMPATIBILITY_TESTS='1'; $env:ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT=$webRoot
    $env:ETRANSLATE_EDITOR_COMPATIBILITY_OUTPUT=$matrix
    $env:PLAYWRIGHT_BROWSERS_PATH=[IO.Path]::GetFullPath((Join-Path $solution '../.local/pdf-browsers'))
    & dotnet test (Join-Path $solution 'tests/ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-build --no-restore --filter 'FullyQualifiedName~Target_browsers_keep_tools_pages_touch_edits_and_locks_reachable' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=browser-matrix.trx' --results-directory $matrix
    if ($LASTEXITCODE -ne 0) { throw "Browser matrix failed: $matrix" }
    if ($BrowserOnly) {
        & $PythonPath (Join-Path $PSScriptRoot 'verify_editor_compatibility.py') $matrix
        if ($LASTEXITCODE -ne 0) { throw 'Browser marker comparison failed.' }
    } else {
        $env:ETRANSLATE_EDITOR_COMPATIBILITY_SQL_TESTS='1'; $env:ETRANSLATE_EDITOR_COMPATIBILITY_SERVER=$SqlServer
        $env:ETRANSLATE_EDITOR_COMPATIBILITY_ROOT=$solution
        $latest=Join-Path $output 'latest-product.json'; if (Test-Path -LiteralPath $latest) { Remove-Item -LiteralPath $latest }
        & dotnet test (Join-Path $solution 'tests/ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-build --no-restore --filter 'FullyQualifiedName~EditorCompatibilityAcceptanceTests' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=product-compatibility.trx' --results-directory $matrix
        if ($LASTEXITCODE -ne 0) { throw 'Product compatibility acceptance failed; inspect the run and cleanup report.' }
        $run=(Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json).runId
        if ($run -notmatch '^[a-f0-9]{32}$') { throw 'Invalid QA run identifier.' }
        $product=Join-Path $solution "tmp/pdfs/editor-compatibility/$run"
        & $PythonPath (Join-Path $PSScriptRoot 'verify_editor_compatibility.py') $matrix $product
        if ($LASTEXITCODE -ne 0) { throw 'Product/browser/PDF comparison or isolation/cleanup failed.' }
        Write-Output "PASS: three real-product cases, only new QA databases removed. Product report: $product"
    }
    Set-Content -LiteralPath (Join-Path $output 'latest-matrix.json') -Value (@{ directory=$matrix } | ConvertTo-Json)
    Write-Output "PASS: eight installed Chrome/Edge cases; mobile sizes/touch are emulated, not real-device acceptance. Matrix: $matrix"
} finally { foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])} }
