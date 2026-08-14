[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ourClassIds = @(
    '{16EAEC3D-A095-4F3D-9D29-FEAC9D26520D}','{961034D4-4D2D-4AD2-AE59-0672E6A3AF99}',
    '{A3D8F82E-0B62-49C7-A20E-A1566F8B4271}','{49724923-52F5-40E4-A743-3F8904BDFF91}'
)
$oldPreviewClassIds = @('{F1A470E4-6AA0-45FC-BE6F-A93E420A7BD7}','{025DEA72-D29D-4DD9-8428-89BC4A2F0D0B}')
$installDirectory = Join-Path $env:ProgramFiles 'Explorer3DPreview'
$thumbnailBackupRoot = 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Explorer3DPreview\ThumbnailBackup'
$legacyBackupRoot = 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Explorer3DPreview\AssociationBackup'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    $elevated = Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    exit $elevated.ExitCode
}
if (-not [Environment]::Is64BitProcess) { throw 'Run the 64-bit version of PowerShell.' }

foreach ($root in @($thumbnailBackupRoot,$legacyBackupRoot)) {
    if (-not (Test-Path $root)) { continue }
    foreach ($backupKey in Get-ChildItem $root) {
        $backup = Get-ItemProperty $backupKey.PSPath
        $path = [string]$backup.Path
        $current = if (Test-Path $path) { (Get-Item $path).GetValue('') } else { $null }
        if ($current -in @($ourClassIds + $oldPreviewClassIds)) {
            if ([int]$backup.HadValue -eq 1) {
                if (-not (Test-Path $path)) { New-Item -Path $path -Force | Out-Null }
                Set-Item -Path $path -Value ([string]$backup.PreviousValue)
            } elseif (Test-Path $path) {
                Remove-Item -Path $path -Force
            }
        }
    }
}
Remove-Item -Path 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Explorer3DPreview' -Recurse -Force -ErrorAction SilentlyContinue

foreach ($installedComHost in @(Get-ChildItem -LiteralPath $installDirectory -Filter 'Explorer3DPreview.comhost.dll' `
    -File -Recurse -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName -Unique)) {
    Start-Process -FilePath "$env:SystemRoot\System32\regsvr32.exe" `
        -ArgumentList '/u', '/s', "`"$installedComHost`"" -Wait | Out-Null
}
Get-Process prevhost -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item -Path 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Explorer3DPreview' `
    -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $installDirectory) { Remove-Item -LiteralPath $installDirectory -Recurse -Force }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Explorer3DShellNotify {
    [DllImport("shell32.dll")] public static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
'@
[Explorer3DShellNotify]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
Write-Host 'Explorer 3D thumbnail handlers were removed.' -ForegroundColor Green
