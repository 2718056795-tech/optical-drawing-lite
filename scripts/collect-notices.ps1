param(
    [Parameter(Mandatory = $true)][string]$AssetsPath,
    [Parameter(Mandatory = $true)][string]$RuntimeDependenciesPath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$sourceNotices = Join-Path (Split-Path $PSScriptRoot -Parent) 'third-party-notices'
$assets = Get-Content -LiteralPath $AssetsPath -Raw | ConvertFrom-Json
$dependencies = Get-Content -LiteralPath $RuntimeDependenciesPath -Raw | ConvertFrom-Json
$packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
$target = $assets.targets.PSObject.Properties | Where-Object { $_.Name -match '/win-x64$' } | Select-Object -First 1
if ($null -eq $target) { throw 'Missing win-x64 restore target in project.assets.json.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $sourceNotices 'license-texts'), (Join-Path $sourceNotices 'Inter'), (Join-Path $sourceNotices 'vendored'), (Join-Path $sourceNotices 'README.md') -Destination $OutputDirectory -Recurse
$records = [Collections.Generic.List[object]]::new()

function Copy-PackageNotices([string]$PackagePath, [string]$Id, [string]$Version) {
    $destination = Join-Path $OutputDirectory "$Id-$Version"
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $nuspec = Get-ChildItem -LiteralPath $PackagePath -Filter '*.nuspec' -File | Select-Object -First 1
    if ($null -eq $nuspec) { throw "Package metadata missing for $Id $Version" }
    [xml]$metadataXml = Get-Content -LiteralPath $nuspec.FullName -Raw
    $metadata = $metadataXml.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
    $license = $metadata.SelectSingleNode('*[local-name()="license"]')
    $expression = ''
    if ($null -ne $license -and $license.GetAttribute('type') -eq 'expression') { $expression = $license.InnerText }
    Copy-Item -LiteralPath $nuspec.FullName -Destination $destination
    $noticeFiles = @(Get-ChildItem -LiteralPath $PackagePath -Recurse -File | Where-Object {
        $_.Name -match '^(LICENSE|LICENCE|COPYING|NOTICE|OFL|THIRD[-_]?PARTY|THIRDPARTY)' -and $_.Extension -ne '.dll'
    })
    if ($null -ne $license -and $license.GetAttribute('type') -eq 'file') {
        $licensePath = [IO.Path]::GetFullPath((Join-Path $PackagePath $license.InnerText))
        if (!$licensePath.StartsWith([IO.Path]::GetFullPath($PackagePath).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "License file outside package: $Id"
        }
        if (!(Test-Path -LiteralPath $licensePath)) { throw "Declared license missing for $Id" }
        $noticeFiles += Get-Item -LiteralPath $licensePath
    }
    foreach ($file in ($noticeFiles | Sort-Object FullName -Unique)) {
        $relative = $file.FullName.Substring($PackagePath.TrimEnd('\').Length + 1)
        $copyPath = Join-Path $destination $relative
        New-Item -ItemType Directory -Path (Split-Path $copyPath -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $copyPath
    }
    foreach ($identifier in ($expression -split '[ ()]+' | Where-Object { $_ -and $_ -notin @('AND', 'OR', 'WITH') })) {
        $fullText = Join-Path $sourceNotices "license-texts\$identifier.txt"
        if (!(Test-Path -LiteralPath $fullText)) { throw "License text not reviewed: $identifier ($Id $Version)" }
        Copy-Item -LiteralPath $fullText -Destination $destination
    }
    if ($noticeFiles.Count -eq 0 -and !$expression) { throw "No license text found for $Id $Version" }
    $copyright = $metadata.SelectSingleNode('*[local-name()="copyright"]')
    $projectUrl = $metadata.SelectSingleNode('*[local-name()="projectUrl"]')
    $records.Add([ordered]@{
        name = $Id
        version = $Version
        licenseExpression = $expression
        copyright = if ($copyright) { $copyright.InnerText } else { '' }
        projectUrl = if ($projectUrl) { $projectUrl.InnerText } else { '' }
        noticeDirectory = "$Id-$Version"
    })
}

foreach ($entry in $dependencies.libraries.PSObject.Properties) {
    if ($entry.Value.type -ne 'package') { continue }
    $library = $assets.libraries.PSObject.Properties[$entry.Name].Value
    $parts = $entry.Name -split '/', 2
    $packagePath = $packageRoots | ForEach-Object { Join-Path $_ $library.path } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (!$packagePath) { throw "Package cache missing: $($entry.Name)" }
    Copy-PackageNotices $packagePath $parts[0] $parts[1]
}

# Runtime packs are restored separately from the regular package target.
$runtimeCount = 0
foreach ($entry in $dependencies.libraries.PSObject.Properties) {
    if (!$entry.Name.StartsWith('runtimepack.', [StringComparison]::OrdinalIgnoreCase)) { continue }
    $parts = $entry.Name.Substring('runtimepack.'.Length) -split '/', 2
    $relative = Join-Path $parts[0].ToLowerInvariant() $parts[1].ToLowerInvariant()
    $runtimePath = $packageRoots | ForEach-Object { Join-Path $_ $relative } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (!$runtimePath) { throw "Runtime pack cache missing: $($entry.Name)" }
    Copy-PackageNotices $runtimePath $parts[0] $parts[1]
    $runtimeCount++
}
if ($runtimeCount -eq 0) { throw 'Runtime pack license missing from restore cache.' }
$records | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'dependencies.json') -Encoding UTF8
