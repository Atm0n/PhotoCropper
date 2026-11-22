<#
.SYNOPSIS
  Makes a folder to copy onto a USB stick and run on a Windows PC with an NVIDIA card, with nothing to install
  there but the NVIDIA driver: the trainer (self-contained, .NET included), its GPU libraries (CUDA 12.8), the
  photos for the album pages, and numbered .bat files to double-click.

.PARAMETER Output
  Where to make the folder. Default: usb\PhotoCropperTrainer next to this script.

.PARAMETER Photos
  A folder of photos (from the trainer's photos command) to include, so the NVIDIA PC needs no internet.

.PARAMETER PackageCache
  Where NuGet keeps downloaded packages. The GPU libraries are several GB: point this at a drive with room.

.EXAMPLE
  .\make-usb.ps1 -Photos .\photos -PackageCache D:\nuget-packages
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot 'usb\PhotoCropperTrainer'),
    [string]$Photos,
    [string]$PackageCache
)

$ErrorActionPreference = 'Stop'
if ($PackageCache) { $env:NUGET_PACKAGES = $PackageCache }

Write-Host "Building the trainer with GPU (CUDA) support into $Output ..."
dotnet publish (Join-Path $PSScriptRoot 'PhotoCropper.Trainer.csproj') -c Release -r win-x64 --self-contained true `
    -p:TorchBackend=cuda -p:DebugType=none -o $Output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

# Double-click scripts. Each runs from its own folder and keeps its window open at the end.
$scripts = [ordered]@{
    '1 - Check this PC.bat'       = 'PhotoCropper.Trainer.exe check'
    '2 - Make album pages.bat'    = 'PhotoCropper.Trainer.exe generate --out albums --count 8000 --size 512 --photos photos'
    '3 - Train.bat'               = 'PhotoCropper.Trainer.exe train --data albums --out models'
    'Preview album pages.bat'     = 'PhotoCropper.Trainer.exe generate --preview preview.jpg --photos photos && start "" preview.jpg'
}
foreach ($name in $scripts.Keys) {
    $body = "@echo off`r`ncd /d `"%~dp0`"`r`n$($scripts[$name])`r`necho.`r`npause`r`n"
    Set-Content -Path (Join-Path $Output $name) -Value $body -Encoding ascii -NoNewline
}
Copy-Item (Join-Path $PSScriptRoot 'USB-README.txt') (Join-Path $Output 'README.txt') -Force

if ($Photos) {
    Write-Host "Copying the photos from $Photos ..."
    $target = Join-Path $Output 'photos'
    New-Item -ItemType Directory -Force $target | Out-Null
    Get-ChildItem $Photos -File | Where-Object { $_.Extension -in '.jpg', '.jpeg', '.png', '.csv' } |
        Copy-Item -Destination $target -Force
}

$size = (Get-ChildItem $Output -Recurse -File | Measure-Object Length -Sum).Sum / 1GB
$largest = (Get-ChildItem $Output -Recurse -File | Sort-Object Length -Descending | Select-Object -First 1)
Write-Host ("Done: {0:N1} GB in {1}" -f $size, $Output)
Write-Host ("Largest file: {0} ({1:N1} GB). A FAT32 stick can't hold files over 4 GB: use exFAT or NTFS if needed." -f $largest.Name, ($largest.Length / 1GB))
