#Requires -Version 7.0
param(
    [string]$NodePath='C:/Users/user/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe',
    [ValidateRange(1024,65535)][int]$Port=5081
)
$ErrorActionPreference='Stop'
if (-not (Test-Path -LiteralPath $NodePath -PathType Leaf)) { throw "Executable not found: $NodePath" }
& $NodePath (Join-Path $PSScriptRoot 'build.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Isolated lab build failed.' }
Write-Output "Synthetic experiment only: http://127.0.0.1:$Port/ - stop with Ctrl+C. No AppHost or SQL required."
& $NodePath (Join-Path $PSScriptRoot 'serve.mjs') --port $Port
