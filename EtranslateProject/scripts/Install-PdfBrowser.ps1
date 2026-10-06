#Requires -Version 7.0
param([string]$BrowserPath = '')
$ErrorActionPreference = 'Stop'
$solutionPath = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($BrowserPath)) {
    $BrowserPath = Join-Path (Split-Path $solutionPath -Parent) '.local/pdf-browsers'
}
$BrowserPath = [IO.Path]::GetFullPath($BrowserPath)
$installer = Join-Path $solutionPath 'src/Services/Documents/ETranslate.Documents.Api/bin/Debug/net10.0/playwright.ps1'
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    throw 'Build ETranslate.slnx in Debug before installing the PDF browser.'
}
$previousPath = $env:PLAYWRIGHT_BROWSERS_PATH
try {
    $env:PLAYWRIGHT_BROWSERS_PATH = $BrowserPath
    # Downloads the native browser matching the pinned Microsoft.Playwright package; no Docker/WSL.
    & $installer install chromium --only-shell
    if ($LASTEXITCODE -ne 0) { throw "PDF browser installation failed (exit $LASTEXITCODE)." }
    Write-Output "PDF browser installed: $BrowserPath"
    Write-Output 'If you used a custom path, configure AppHost PdfBrowserPath to the same absolute path.'
} finally {
    $env:PLAYWRIGHT_BROWSERS_PATH = $previousPath
}
