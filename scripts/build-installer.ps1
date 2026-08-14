[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$setupProject = Join-Path $root 'Explorer3DPreview.Setup\Explorer3DPreview.Setup.csproj'
$publishDirectory = Join-Path $root 'artifacts\setup-publish'
$verifyDirectory = Join-Path $root 'artifacts\setup-verify'
$installerPath = Join-Path $root 'artifacts\STL-3MF-Thumbnail-Preview-Windows-1.2.4.exe'
$legacyIExpressDirectory = Join-Path $root 'artifacts\installer-build'

& (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $verifyDirectory) {
    Remove-Item -LiteralPath $verifyDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $legacyIExpressDirectory) {
    Remove-Item -LiteralPath $legacyIExpressDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $installerPath) {
    Remove-Item -LiteralPath $installerPath -Force
}

& dotnet clean $setupProject -c $Configuration -r win-x64
if ($LASTEXITCODE -ne 0) { throw 'Installer clean failed.' }
& dotnet publish $setupProject -c $Configuration -r win-x64 --self-contained false -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }

$setupAssembly = Join-Path $root "Explorer3DPreview.Setup\bin\$Configuration\net6.0-windows\win-x64\Explorer3DPreview-Setup.dll"
& dotnet exec $setupAssembly --extract-payload $verifyDirectory
if ($LASTEXITCODE -ne 0) { throw 'Embedded payload extraction failed.' }

$payloadSources = @{
    'Explorer3DPreview.comhost.dll' = Join-Path $root "Explorer3DPreview\bin\x64\$Configuration\net6.0-windows\Explorer3DPreview.comhost.dll"
    'Explorer3DPreview.deps.json' = Join-Path $root "Explorer3DPreview\bin\x64\$Configuration\net6.0-windows\Explorer3DPreview.deps.json"
    'Explorer3DPreview.dll' = Join-Path $root "Explorer3DPreview\bin\x64\$Configuration\net6.0-windows\Explorer3DPreview.dll"
    'Explorer3DPreview.runtimeconfig.json' = Join-Path $root "Explorer3DPreview\bin\x64\$Configuration\net6.0-windows\Explorer3DPreview.runtimeconfig.json"
    'Explorer3DPreview.ico' = Join-Path $root 'assets\Explorer3DPreview.ico'
    'install.ps1' = Join-Path $PSScriptRoot 'install.ps1'
    'uninstall.ps1' = Join-Path $PSScriptRoot 'uninstall.ps1'
}
foreach ($file in $payloadSources.Keys) {
    $sourceHash = (Get-FileHash -LiteralPath $payloadSources[$file] -Algorithm SHA256).Hash
    $embeddedHash = (Get-FileHash -LiteralPath (Join-Path $verifyDirectory $file) -Algorithm SHA256).Hash
    if ($sourceHash -ne $embeddedHash) { throw "Embedded payload is stale: $file" }
}
Remove-Item -LiteralPath $verifyDirectory -Recurse -Force

$publishedExe = Join-Path $publishDirectory 'Explorer3DPreview-Setup.exe'
if (-not (Test-Path -LiteralPath $publishedExe)) {
    throw 'Published installer executable was not created.'
}
Copy-Item -LiteralPath $publishedExe -Destination $installerPath

$installer = Get-Item -LiteralPath $installerPath
if ($installer.Length -lt 100KB) {
    throw 'Installer is unexpectedly small; embedded payload verification failed.'
}
Write-Host "Installer ready: $($installer.FullName) ($([Math]::Round($installer.Length / 1KB)) KB)" -ForegroundColor Green
