param(
    [string]$Dotnet = 'dotnet',
    [string]$Output,
    [string]$PackageDirectory = (Join-Path $PSScriptRoot 'artifacts\packages')
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\OtoCAD.Avalonia\OtoCAD.Avalonia.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $PSScriptRoot "artifacts\win-x64-v$version"
}
$Output = [IO.Path]::GetFullPath($Output)
$PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
if ((Test-Path -LiteralPath $Output) -and (Get-ChildItem -LiteralPath $Output -Force | Select-Object -First 1)) {
    throw "Output must be empty to keep personal data out of release packages: $Output"
}
$packageName = "optical-drawing-lite-v$version-win-x64"
$archive = Join-Path $PackageDirectory "$packageName.zip"
if (Test-Path -LiteralPath $archive) { throw "Release package already exists: $archive" }

# Keep the portable runtime at least as recent as the previous release when building
# with the oldest supported SDK. Later SDK patches choose their own servicing runtime.
$sdkVersion = ((& $Dotnet --version) | Out-String).Trim()
$runtimeOptions = @()
if ($sdkVersion -eq '10.0.403') { $runtimeOptions += '-p:RuntimeFrameworkVersion=10.0.15' }
& $Dotnet publish $project @runtimeOptions -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:PublishReadyToRun=false -p:DebugType=embedded -o $Output
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

Copy-Item -LiteralPath @((Join-Path $PSScriptRoot 'LICENSE'), (Join-Path $PSScriptRoot 'NOTICE.md'), (Join-Path $PSScriptRoot 'README.md')) -Destination $Output
$assets = Join-Path $PSScriptRoot 'src\BuildOutput\obj\OtoCAD.Avalonia\project.assets.json'
$runtimeDependencies = Join-Path $PSScriptRoot 'src\BuildOutput\Release\OtoCAD.Avalonia\net10.0\win-x64\光学出图简版.deps.json'
& (Join-Path $PSScriptRoot 'scripts\collect-notices.ps1') -AssetsPath $assets -RuntimeDependenciesPath $runtimeDependencies -OutputDirectory (Join-Path $Output 'third-party-notices')

$files = @(Get-ChildItem -LiteralPath $Output -Recurse -File | Sort-Object FullName | ForEach-Object {
    [ordered]@{
        path = $_.FullName.Substring($Output.Length + 1).Replace('\', '/')
        bytes = $_.Length
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
})
$manifest = [ordered]@{
    version = $version
    runtimeIdentifier = 'win-x64'
    selfContained = $true
    sdkVersion = $sdkVersion
    executableSignature = [string](Get-AuthenticodeSignature -LiteralPath (Join-Path $Output '光学出图简版.exe')).Status
    files = $files
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Output 'release-manifest.json') -Encoding UTF8

New-Item -ItemType Directory -Path $PackageDirectory -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($Output, $archive, [IO.Compression.CompressionLevel]::Optimal, $true)
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $packageName.zip" | Set-Content -LiteralPath "$archive.sha256" -Encoding ASCII
Write-Output "Application: $Output"
Write-Output "Package: $archive"
Write-Output "SHA256: $hash"
