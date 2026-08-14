[CmdletBinding()]
param([string]$ExtensionsFile)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$meshClassId = '{16EAEC3D-A095-4F3D-9D29-FEAC9D26520D}'
$embeddedClassId = '{961034D4-4D2D-4AD2-AE59-0672E6A3AF99}'
$oldThumbnailClassIds = @('{A3D8F82E-0B62-49C7-A20E-A1566F8B4271}','{49724923-52F5-40E4-A743-3F8904BDFF91}')
$oldPreviewClassIds = @('{F1A470E4-6AA0-45FC-BE6F-A93E420A7BD7}','{025DEA72-D29D-4DD9-8428-89BC4A2F0D0B}')
$thumbnailInterface = '{E357FCCD-A995-4576-B01F-234630154E96}'
$previewInterface = '{8895B1C6-B41F-4C1C-A562-0D564250836F}'
$installDirectory = Join-Path $env:ProgramFiles 'Explorer3DPreview'
$packageVersion = '1.2.3'
$packagesDirectory = Join-Path $installDirectory 'packages'
# Explorer may keep the current COM host loaded for the whole session. Deploying each
# install to a fresh directory makes repair/reinstall safe without killing Explorer.
$versionDirectory = Join-Path $packagesDirectory ("$packageVersion-$([Guid]::NewGuid().ToString('N'))")
$thumbnailBackupRoot = 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Explorer3DPreview\ThumbnailBackup'
$legacyBackupRoot = 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Explorer3DPreview\AssociationBackup'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    if (-not [string]::IsNullOrWhiteSpace($ExtensionsFile)) { $arguments += " -ExtensionsFile `"$ExtensionsFile`"" }
    $elevated = Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    exit $elevated.ExitCode
}
if (-not [Environment]::Is64BitProcess) { throw 'Run the 64-bit version of PowerShell.' }

trap {
    $details = ($_ | Format-List * -Force | Out-String)
    try { Set-Content -LiteralPath (Join-Path $PSScriptRoot 'install-error.log') -Value $details -Encoding UTF8 }
    catch { }
    Write-Error $details
    exit 1
}

$runtimeInstalled = (& dotnet --list-runtimes 2>$null) -match '^Microsoft\.WindowsDesktop\.App 6\.'
if (-not $runtimeInstalled) {
    throw '.NET 6 Desktop Runtime x64 was not found. Install it from https://dotnet.microsoft.com/download/dotnet/6.0'
}

$directExtensions = @('.stl','.3mf','.obj','.ply','.amf','.off','.gcode','.gco')
$embeddedExtensions = @('.sldprt','.sdlprt','.sldasm','.slddrw','.prtdot','.asmdot','.drwdot','.eprt','.easm','.edrw')
$externalExtensions = @(
    '.step','.stp','.stpz','.iges','.igs','.x_t','.x_b','.xmt_txt','.xmt_bin','.sat','.sab',
    '.ifc','.vda','.wrl','.vrml','.3dxml','.jt','.3dm','.catpart','.catproduct','.ipt','.iam',
    '.prt','.asm','.neu','.xpr','.xas','.par','.psm','.pwd','.dxf','.dwg'
)
$allExtensions = @($directExtensions + $embeddedExtensions + $externalExtensions)

$selectionStore = Join-Path $installDirectory 'extensions.txt'
if (-not [string]::IsNullOrWhiteSpace($ExtensionsFile) -and (Test-Path -LiteralPath $ExtensionsFile)) {
    $selectedExtensions = @(Get-Content -LiteralPath $ExtensionsFile)
} elseif (Test-Path -LiteralPath $selectionStore) {
    $selectedExtensions = @(Get-Content -LiteralPath $selectionStore)
} else {
    $selectedExtensions = @($allExtensions)
}
$selectedExtensions = @($selectedExtensions | ForEach-Object { $_.Trim().ToLowerInvariant() } |
    Where-Object { $_ -in $allExtensions } | Select-Object -Unique)
if ($selectedExtensions.Count -eq 0) { throw 'No supported file extensions were selected.' }

function Restore-BackupKey {
    param([Parameter(Mandatory)]$BackupKey, [Parameter(Mandatory)][string[]]$OurClassIds)
    $backup = Get-ItemProperty $BackupKey.PSPath
    $path = [string]$backup.Path
    $current = if (Test-Path $path) { (Get-Item $path).GetValue('') } else { $null }
    if ($current -in $OurClassIds) {
        if ([int]$backup.HadValue -eq 1) {
            if (-not (Test-Path $path)) { New-Item -Path $path -Force | Out-Null }
            Set-Item -Path $path -Value ([string]$backup.PreviousValue)
        } elseif (Test-Path $path) {
            Remove-Item -Path $path -Force
        }
    }
    Remove-Item -Path $BackupKey.PSPath -Recurse -Force -ErrorAction SilentlyContinue
}

# Migrate 1.0/1.1: remove the heavy Alt+P preview handlers and restore what they replaced.
Get-Process prevhost -ErrorAction SilentlyContinue | Stop-Process -Force
if (Test-Path $legacyBackupRoot) {
    Get-ChildItem $legacyBackupRoot | ForEach-Object { Restore-BackupKey -BackupKey $_ -OurClassIds $oldPreviewClassIds }
    Remove-Item -Path $legacyBackupRoot -Recurse -Force -ErrorAction SilentlyContinue
}
foreach ($extension in $allExtensions) {
    $extensionKey = "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\$extension"
    $progId = if (Test-Path $extensionKey) { (Get-Item $extensionKey).GetValue('') } else { $null }
    $paths = @(
        "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\SystemFileAssociations\$extension\shellex\$previewInterface",
        "$extensionKey\shellex\$previewInterface"
    )
    if (-not [string]::IsNullOrWhiteSpace([string]$progId)) {
        $paths += "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\$progId\shellex\$previewInterface"
    }
    foreach ($path in $paths) {
        if ((Test-Path $path) -and ((Get-Item $path).GetValue('') -in $oldPreviewClassIds)) {
            Remove-Item -Path $path -Force
        }
    }
}
$previewHandlers = 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\PreviewHandlers'
if (Test-Path $previewHandlers) {
    foreach ($classId in $oldPreviewClassIds) {
        Remove-ItemProperty -Path $previewHandlers -Name $classId -ErrorAction SilentlyContinue
    }
}

$sourceComHost = Join-Path $PSScriptRoot 'Explorer3DPreview.comhost.dll'
if (-not (Test-Path -LiteralPath $sourceComHost)) {
    throw 'Explorer3DPreview.comhost.dll is missing. Run scripts\build.ps1 first.'
}
New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $packagesDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $versionDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot 'Explorer3DPreview*') -Destination $versionDirectory -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $installDirectory -Force
Set-Content -LiteralPath $selectionStore -Value $selectedExtensions -Encoding ASCII

$installedComHost = Join-Path $versionDirectory 'Explorer3DPreview.comhost.dll'
$registration = Start-Process -FilePath "$env:SystemRoot\System32\regsvr32.exe" `
    -ArgumentList '/s', "`"$installedComHost`"" -Wait -PassThru
if ($registration.ExitCode -ne 0) { throw "regsvr32 returned exit code $($registration.ExitCode)." }

foreach ($handler in @(
    @{ Id = $meshClassId; Name = 'Explorer 3D Mesh Thumbnail Provider' },
    @{ Id = $embeddedClassId; Name = 'Explorer 3D Embedded Thumbnail Provider' }
)) {
    $classKey = "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\$($handler.Id)"
    if (-not (Test-Path $classKey)) { New-Item -Path $classKey -Force | Out-Null }
    Set-Item -Path $classKey -Value $handler.Name
    $inprocKey = Join-Path $classKey 'InprocServer32'
    if (-not (Test-Path $inprocKey)) { New-Item -Path $inprocKey -Force | Out-Null }
    Set-Item -Path $inprocKey -Value $installedComHost
    New-ItemProperty -Path $inprocKey -Name 'ThreadingModel' -Value 'Apartment' -PropertyType String -Force | Out-Null
}

function Save-And-SetThumbnailAssociation {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$ClassId,
        [Parameter(Mandatory)][string]$BackupName)
    $current = if (Test-Path $Path) { (Get-Item $Path).GetValue('') } else { $null }
    if ($current -eq $ClassId) { return }
    $backupKey = Join-Path $thumbnailBackupRoot $BackupName
    if (-not (Test-Path $backupKey)) {
        New-Item -Path $backupKey -Force | Out-Null
        New-ItemProperty -Path $backupKey -Name 'Path' -Value $Path -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $backupKey -Name 'HadValue' -Value ([int]($null -ne $current)) -PropertyType DWord -Force | Out-Null
        if ($null -ne $current) {
            New-ItemProperty -Path $backupKey -Name 'PreviousValue' -Value ([string]$current) -PropertyType String -Force | Out-Null
        }
    }
    if (-not (Test-Path $Path)) { New-Item -Path $Path -Force | Out-Null }
    Set-Item -Path $Path -Value $ClassId
}

foreach ($extension in $allExtensions) {
    if ($selectedExtensions -contains $extension) { continue }
    $safeName = $extension.TrimStart('.')
    if (Test-Path $thumbnailBackupRoot) {
        Get-ChildItem $thumbnailBackupRoot | Where-Object { $_.PSChildName -like "${safeName}_*" } |
            ForEach-Object { Restore-BackupKey -BackupKey $_ -OurClassIds (@($meshClassId,$embeddedClassId) + $oldThumbnailClassIds) }
    }
}

$registeredCount = 0
$preservedCount = 0
foreach ($extension in $selectedExtensions) {
    $extensionKey = "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\$extension"
    $progId = if (Test-Path $extensionKey) { (Get-Item $extensionKey).GetValue('') } else { $null }
    $paths = @(
        "$extensionKey\shellex\$thumbnailInterface",
        "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\SystemFileAssociations\$extension\shellex\$thumbnailInterface"
    )
    if (-not [string]::IsNullOrWhiteSpace([string]$progId)) {
        $paths += "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\$progId\shellex\$thumbnailInterface"
    }
    $existing = @($paths | Where-Object { Test-Path $_ } | ForEach-Object { (Get-Item $_).GetValue('') } |
        Where-Object { $null -ne $_ -and $_ -notin @($meshClassId,$embeddedClassId) -and $_ -notin $oldThumbnailClassIds })
    if ($existing.Count -gt 0) {
        $preservedCount++
        continue
    }

    $classId = if ($extension -in $directExtensions) { $meshClassId }
        elseif ($extension -in $embeddedExtensions) { $embeddedClassId }
        else { $null }
    if ($null -eq $classId) { continue }

    $safeName = $extension.TrimStart('.')
    Save-And-SetThumbnailAssociation -Path $paths[0] -ClassId $classId -BackupName "${safeName}_extension"
    Save-And-SetThumbnailAssociation -Path $paths[1] -ClassId $classId -BackupName "${safeName}_system"
    if ($paths.Count -gt 2) {
        Save-And-SetThumbnailAssociation -Path $paths[2] -ClassId $classId -BackupName "${safeName}_progid"
    }
    $registeredCount++
}

$explorerAdvanced = 'Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
if (-not (Test-Path $explorerAdvanced)) { New-Item -Path $explorerAdvanced -Force | Out-Null }
New-ItemProperty -Path $explorerAdvanced -Name 'IconsOnly' -Value 0 -PropertyType DWord -Force | Out-Null

$uninstallKey = 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Explorer3DPreview'
$uninstallScript = Join-Path $installDirectory 'uninstall.ps1'
$powerShellExe = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$uninstallCommand = "`"$powerShellExe`" -NoProfile -ExecutionPolicy Bypass -File `"$uninstallScript`""
$displayIcon = Join-Path $versionDirectory 'Explorer3DPreview.ico'
$installedSizeKb = [Math]::Ceiling(((Get-ChildItem $installDirectory -File -Recurse | Measure-Object Length -Sum).Sum) / 1KB)
New-Item -Path $uninstallKey -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'DisplayName' -Value 'STL & 3MF Thumbnail Preview for Windows' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'DisplayVersion' -Value $packageVersion -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'Publisher' -Value 'STL & 3MF Thumbnail Preview' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'InstallLocation' -Value $installDirectory -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'DisplayIcon' -Value $displayIcon -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'UninstallString' -Value $uninstallCommand -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'QuietUninstallString' -Value $uninstallCommand -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'InstallDate' -Value (Get-Date -Format 'yyyyMMdd') -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'EstimatedSize' -Value ([int]$installedSizeKb) -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'NoModify' -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'NoRepair' -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'SelectedExtensions' -Value ($selectedExtensions -join ';') -PropertyType String -Force | Out-Null

# Remember the active package, then clean up copies that are no longer loaded. A
# locked previous package is harmless and will be retried on the next install.
Set-Content -LiteralPath (Join-Path $installDirectory 'current-package.txt') -Value $versionDirectory -Encoding ASCII
Get-ChildItem -LiteralPath $packagesDirectory -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -ne $versionDirectory } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
# Remove pre-1.2.3 version folders when they are not held by an older Shell process.
Get-ChildItem -LiteralPath $installDirectory -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne 'packages' } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Explorer3DShellNotify {
    [DllImport("shell32.dll")] public static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
'@
[Explorer3DShellNotify]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
Write-Host "Installed: $registeredCount thumbnail providers added; $preservedCount existing providers preserved." -ForegroundColor Green
