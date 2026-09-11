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

function Publish-Project {
    param (
        [string]$Project,
        [string]$Runtime,
        [string]$OutputFolder
    )

    $ProjectName = [System.IO.Path]::GetFileNameWithoutExtension($Project)
    Write-Host "----------------------------------------------------" -ForegroundColor Green
    Write-Host "Publishing $ProjectName for $Runtime..." -ForegroundColor Green
    Write-Host "----------------------------------------------------" -ForegroundColor Green

    $OutputPath = Join-Path $PublishDir $OutputFolder

    dotnet publish $Project `
        -c Release `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishReadyToRun=true `
        -o $OutputPath

    if ($LASTEXITCODE -eq 0) {
        Write-Host "Successfully published $ProjectName ($Runtime) to $OutputPath" -ForegroundColor Green
    } else {
        Write-Host "Failed to publish $ProjectName for $Runtime" -ForegroundColor Red
    }
}

# Publish GUI and CLI for Windows x64
Publish-Project -Project "PhotoCropperGui/PhotoCropperGui.csproj" -Runtime "win-x64" -OutputFolder "windows"
Publish-Project -Project "PhotoCropperCli/PhotoCropperCli.csproj" -Runtime "win-x64" -OutputFolder "windows"

# Publish GUI and CLI for Linux x64
Publish-Project -Project "PhotoCropperGui/PhotoCropperGui.csproj" -Runtime "linux-x64" -OutputFolder "linux"
Publish-Project -Project "PhotoCropperCli/PhotoCropperCli.csproj" -Runtime "linux-x64" -OutputFolder "linux"

Write-Host "`nAll tasks complete. Check the '$PublishDir' folder for results." -ForegroundColor Cyan
