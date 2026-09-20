[CmdletBinding()]
param([string]$output_directory, [string[]]$package_ids = @())

$ErrorActionPreference = 'Stop'
$repo_root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($output_directory)) {
    $output_directory = Join-Path $repo_root 'artifacts'
}
$packages_root = Join-Path $repo_root 'packages'
$package_directories = @(Get-ChildItem -LiteralPath $packages_root -Directory | Sort-Object Name)
foreach ($id in $package_ids) {
    if ($id -notin $package_directories.Name) { throw "Unknown package: $id" }
}
$plans = @()
$guid_paths = @{}
$external_vpm_dependencies = @('com.vrchat.avatars')
foreach ($directory in $package_directories) {
    $manifest = Get-Content -LiteralPath (Join-Path $directory.FullName 'package.json') -Raw | ConvertFrom-Json
    if ($manifest.name -cne $directory.Name) { throw "Package name mismatch: $($directory.Name)" }
    if ($manifest.version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Invalid package version' }
    if ([string]::IsNullOrWhiteSpace($manifest.author.name) -or [string]::IsNullOrWhiteSpace($manifest.author.email)) {
        throw "Missing public author details: $($manifest.name)"
    }
    if ($manifest.license -ne 'MIT' -or -not (Test-Path -LiteralPath (Join-Path $directory.FullName 'LICENSE.md'))) {
        throw "Missing approved MIT license: $($manifest.name)"
    }
    $zip_name = "$($manifest.name)-$($manifest.version).zip"
    if (-not $manifest.url.EndsWith('/' + $zip_name)) { throw 'Download URL does not match ZIP name' }
    $zip_path = Join-Path $output_directory $zip_name
    $selected = $package_ids.Count -eq 0 -or $manifest.name -in $package_ids
    if ($selected -and (Test-Path -LiteralPath $zip_path)) { throw "ZIP already exists: $zip_path" }
    $files = @(Get-ChildItem -LiteralPath $directory.FullName -Recurse -File)
    foreach ($file in $files) {
        if ($file.Extension -eq '.meta') {
            $match = [regex]::Match((Get-Content -LiteralPath $file.FullName -Raw), '(?m)^guid: ([0-9a-f]{32})\s*$')
            if (-not $match.Success) { throw "Missing GUID: $($file.FullName)" }
            $guid = $match.Groups[1].Value
            if ($guid_paths.ContainsKey($guid)) { throw "Duplicate GUID: $($file.FullName)" }
            $guid_paths[$guid] = $file.FullName
        } elseif (-not (Test-Path -LiteralPath ($file.FullName + '.meta'))) {
            throw "Missing .meta: $($file.FullName)"
        }
    }
    foreach ($definition in ($files | Where-Object Extension -eq '.asmdef')) {
        $assembly = Get-Content -LiteralPath $definition.FullName -Raw | ConvertFrom-Json
        if (@($assembly.includePlatforms).Count -ne 1 -or $assembly.includePlatforms[0] -ne 'Editor') {
            throw "Assembly is not Editor-only: $($definition.Name)"
        }
    }
    foreach ($dependency in $manifest.vpmDependencies.PSObject.Properties) {
        if ($dependency.Name -notin $external_vpm_dependencies -and -not (Test-Path -LiteralPath (Join-Path $packages_root $dependency.Name))) {
            throw "Missing local dependency: $($dependency.Name)"
        }
    }
    if ($selected) {
        $plans += [pscustomobject]@{ directory=$directory.FullName; manifest=$manifest; zip_path=$zip_path }
    }
}
New-Item -ItemType Directory -Path $output_directory -Force | Out-Null
$report = @()
foreach ($plan in $plans) {
    Compress-Archive -Path (Join-Path $plan.directory '*') -DestinationPath $plan.zip_path -CompressionLevel Optimal
    $report += [pscustomobject]@{
        package=$plan.manifest.name; version=$plan.manifest.version
        zip=(Split-Path -Leaf $plan.zip_path)
        bytes=(Get-Item -LiteralPath $plan.zip_path).Length
        sha256=(Get-FileHash -LiteralPath $plan.zip_path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output_directory 'package_artifacts.json') -Encoding UTF8
$report | Format-Table package,version,bytes
