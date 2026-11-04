#Requires -Version 7.0
$ErrorActionPreference='Stop'
$solution=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output=Join-Path $solution 'tmp/dependency-security'; New-Item -ItemType Directory -Force $output | Out-Null
$restore=Join-Path $output 'restore.log'
& dotnet restore (Join-Path $solution 'ETranslate.slnx') --force-evaluate -p:NuGetAudit=true -p:NuGetAuditMode=all *> $restore
if($LASTEXITCODE -ne 0){throw "Restore failed: $restore"}
# Failure to obtain vulnerability information must not be reported as a clean audit.
if(Select-String -LiteralPath $restore -Pattern '\bNU190[0-5]\b' -Quiet){throw "Audit warning/error: $restore"}
$auditPath=Join-Path $output 'audit.json'
& dotnet list (Join-Path $solution 'ETranslate.slnx') package --vulnerable --include-transitive --format json --no-restore > $auditPath
if($LASTEXITCODE -ne 0){throw 'Vulnerability query failed.'}
$audit=Get-Content -LiteralPath $auditPath -Raw | ConvertFrom-Json
if($audit.version -ne 1 -or @($audit.logs).Count -gt 0 -and $null -ne $audit.logs){throw 'Audit format or source query failed.'}
[xml]$sln=Get-Content -LiteralPath (Join-Path $solution 'ETranslate.slnx') -Raw
$projects=@($sln.SelectNodes('//Project') | ForEach-Object {[IO.Path]::GetFullPath((Join-Path $solution $_.Path))})
$reported=@($audit.projects | ForEach-Object {[IO.Path]::GetFullPath($_.path)})
if(@(Compare-Object $projects $reported).Count -ne 0){throw 'Audit did not cover the complete solution.'}
$resolved=@()
foreach($project in $audit.projects){
    foreach($framework in $project.frameworks){foreach($package in @($framework.topLevelPackages)+@($framework.transitivePackages)){
        if($null -ne $package -and @($package.vulnerabilities).Count -gt 0 -and $null -ne $package.vulnerabilities){throw 'A known vulnerable dependency remains.'}
    }}
    $assetsPath=Join-Path (Split-Path $project.path -Parent) 'obj/project.assets.json'
    $assets=Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    if(@($assets.logs | Where-Object code -Match '^NU190[0-5]$').Count){throw 'Dependency assets contain unresolved audit warnings.'}
    foreach($item in $assets.libraries.PSObject.Properties.Name | Where-Object {$_ -match '^(System.Security.Cryptography.Pkcs|MessagePack)/'}){
        if($item -notin @('System.Security.Cryptography.Pkcs/9.0.21','MessagePack/2.5.303')){throw 'Unexpected security override resolution; review updated versions explicitly.'}
        $resolved+=@{project=$project.path;package=$item}
    }
}
@{checkedAtUtc=[DateTimeOffset]::UtcNow;projectCount=$projects.Count;knownVulnerabilities=0;resolved=$resolved} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'summary.json')
Write-Output "PASS: $($projects.Count) projects, direct/transitive packages audited, no known vulnerabilities reported; overrides verified. Output: $output"
