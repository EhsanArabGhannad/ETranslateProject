#Requires -Version 7.0
param(
    [string]$SqlServer='.\ESIMSSQLSERVER',
    [string]$NodePath='C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe',
    [string]$PythonPath='C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe',
    [string]$BaselineDirectory='',
    [switch]$BrowserOnly
)
$ErrorActionPreference='Stop'
if ($SqlServer -notmatch '^\.(\\[A-Za-z0-9_]+)?$') { throw 'Only a local SQL instance is allowed.' }
$solution=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$web=Join-Path $solution 'src/Web/ETranslate.Web'
$output=Join-Path $solution 'tmp/editor-performance'
$browserOutput=Join-Path $output ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $browserOutput | Out-Null
foreach($path in @($NodePath,$PythonPath)){if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Executable missing: $path"}}
if ($BaselineDirectory -and -not (Test-Path -LiteralPath $BaselineDirectory -PathType Container)) { throw 'Baseline directory does not exist.' }
Push-Location $web
try {
    & $NodePath --test 'ClientApp/tests/*.test.js'; if ($LASTEXITCODE -ne 0) { throw 'Client tests failed.' }
    & $NodePath ClientApp/build.mjs; if ($LASTEXITCODE -ne 0) { throw 'Client build failed.' }
} finally { Pop-Location }
& dotnet build (Join-Path $solution 'ETranslate.slnx') --no-restore -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
$names=@('ETRANSLATE_EDITOR_PERFORMANCE_TESTS','ETRANSLATE_EDITOR_PERFORMANCE_BASELINE','ETRANSLATE_EDITOR_PERFORMANCE_OUTPUT',
    'ETRANSLATE_EDITOR_PERFORMANCE_SQL_TESTS','ETRANSLATE_EDITOR_PERFORMANCE_SERVER','ETRANSLATE_EDITOR_PERFORMANCE_ROOT',
    'ETRANSLATE_EDITOR_ADAPTER_TESTS','ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT','PLAYWRIGHT_BROWSERS_PATH')
$previous=@{};foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
try {
    $env:ETRANSLATE_EDITOR_PERFORMANCE_TESTS='1'; $env:ETRANSLATE_EDITOR_PERFORMANCE_BASELINE=$null
    $env:ETRANSLATE_EDITOR_PERFORMANCE_OUTPUT=$browserOutput; $env:ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT=$web
    $env:ETRANSLATE_EDITOR_ADAPTER_TESTS='1'; $env:PLAYWRIGHT_BROWSERS_PATH=[IO.Path]::GetFullPath((Join-Path $solution '../.local/pdf-browsers'))
    $filter=@('Large_documents_measure_input_view_work','Pagination_cache_invalidates',
        'Production_pagination_matches_the_new_pdf','Unsupported_pagination_falls_back',
        'Paginated_template_regions_follow_last_page_footer','Pagination_measures_private_images',
        'Manual_page_break_commands_keyboard_history','Search_replace_preserves_marks_history',
        'Column_percentages_preserve_content_selection','Horizontal_merge_and_split_preserve',
        'Private_image_upload_preserves_anchor','Paper_zoom_and_focus_are_view_only') | ForEach-Object { "FullyQualifiedName~$_" }
    & dotnet test (Join-Path $solution 'tests/ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-build --no-restore --filter ($filter -join '|') --logger 'console;verbosity=normal' --logger 'trx;LogFileName=performance-browser.trx' --results-directory $browserOutput
    if ($LASTEXITCODE -ne 0) { throw "Performance/geometry browser checks failed: $browserOutput" }
    & $PythonPath (Join-Path $PSScriptRoot 'verify_product_pagination.py') (Join-Path $solution 'tmp/editor-adapter')
    if ($LASTEXITCODE -ne 0) { throw 'Existing product pagination marker regression failed.' }
    $product='-'
    if (-not $BrowserOnly) {
        $env:ETRANSLATE_EDITOR_PERFORMANCE_SQL_TESTS='1'; $env:ETRANSLATE_EDITOR_PERFORMANCE_SERVER=$SqlServer; $env:ETRANSLATE_EDITOR_PERFORMANCE_ROOT=$solution
        $latest=Join-Path $output 'latest-product.json'; if(Test-Path -LiteralPath $latest){Remove-Item -LiteralPath $latest}
        & dotnet test (Join-Path $solution 'tests/ETranslate.DomainTests/ETranslate.DomainTests.csproj') --no-build --no-restore --filter 'FullyQualifiedName~EditorPerformanceAcceptanceTests' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=performance-product.trx' --results-directory $browserOutput
        if ($LASTEXITCODE -ne 0) { throw 'Product performance acceptance failed; inspect the run and cleanup report.' }
        $run=(Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json).runId
        if($run -notmatch '^[a-f0-9]{32}$'){throw 'Invalid QA run identifier.'}
        $product=Join-Path $solution "tmp/pdfs/editor-performance/$run"
    }
    $comparisonArgs=@((Join-Path $PSScriptRoot 'verify_editor_performance.py'),$browserOutput,$product)
    if($BaselineDirectory){$comparisonArgs += [IO.Path]::GetFullPath($BaselineDirectory)}
    & $PythonPath @comparisonArgs
    if ($LASTEXITCODE -ne 0) { throw 'Performance, editor/PDF parity or cleanup check failed.' }
    Set-Content -LiteralPath (Join-Path $output 'latest-browser.json') -Value (@{directory=$browserOutput;product=$product}|ConvertTo-Json)
    Write-Output "PASS: local performance budgets and marker pages; no production capacity claim. Browser: $browserOutput Product: $product"
} finally {foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])}}
