[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'Explorer3DPreview.sln'
$output = Join-Path $root 'artifacts\Explorer3DPreview'

& (Join-Path $PSScriptRoot 'build-icon.ps1')
if (-not (Test-Path -LiteralPath (Join-Path $root 'assets\Explorer3DPreview.ico'))) { throw 'Icon build failed.' }

& dotnet build $solution -c $Configuration -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
New-Item -ItemType Directory -Path $output | Out-Null

$buildOutput = Join-Path $root "Explorer3DPreview\bin\x64\$Configuration\net6.0-windows"
Copy-Item -Path (Join-Path $buildOutput '*') -Destination $output -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1') -Destination $output
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $output
Copy-Item -LiteralPath (Join-Path $root 'assets\Explorer3DPreview.ico') -Destination $output

Write-Host "Package ready: $output" -ForegroundColor Green
