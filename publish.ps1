# PhotoCropper Cross-Platform Publish Script
# This script builds self-contained, single-file executables for Windows and Linux.

$PublishDir = "publish"
$ProjectFile = "PhotoCropperGui/PhotoCropperGui.csproj"

# Ensure publish directory exists and is clean
if (Test-Path $PublishDir) {
    Write-Host "Cleaning existing publish directory..." -ForegroundColor Cyan
    Remove-Item -Recurse -Force $PublishDir
}
New-Item -ItemType Directory -Path $PublishDir | Out-Null

function Publish-App {
    param (
        [string]$Runtime,
        [string]$OutputFolder
    )

    Write-Host "----------------------------------------------------" -ForegroundColor Green
    Write-Host "Publishing for $Runtime..." -ForegroundColor Green
    Write-Host "----------------------------------------------------" -ForegroundColor Green

    $OutputPath = Join-Path $PublishDir $OutputFolder

    dotnet publish $ProjectFile `
        -c Release `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishReadyToRun=true `
        -o $OutputPath

    if ($LASTEXITCODE -eq 0) {
        Write-Host "Successfully published $Runtime to $OutputPath" -ForegroundColor Green
    } else {
        Write-Host "Failed to publish for $Runtime" -ForegroundColor Red
    }
}

# Publish for Windows x64
Publish-App -Runtime "win-x64" -OutputFolder "windows"

# Publish for Linux x64
Publish-App -Runtime "linux-x64" -OutputFolder "linux"

Write-Host "`nAll tasks complete. Check the '$PublishDir' folder for results." -ForegroundColor Cyan
