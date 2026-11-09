param(
    [string]$Dotnet = 'dotnet',
    [string]$Output = (Join-Path $PSScriptRoot 'artifacts\win-x64')
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\OtoCAD.Avalonia\OtoCAD.Avalonia.csproj'
& $Dotnet publish $project -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded -o $Output
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath @((Join-Path $PSScriptRoot 'LICENSE'), (Join-Path $PSScriptRoot 'NOTICE.md')) -Destination $Output
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'third-party-notices') -Destination $Output -Recurse -Force
