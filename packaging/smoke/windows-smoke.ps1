<#
Smoke-tests the Windows installer the way a user gets it: installs it silently into a temporary folder, runs the
installed CLI on the sample scans, checks the GUI starts, then uninstalls. It catches packaging mistakes the unit
tests can't see, such as a native library missing from the installed app.

  packaging/smoke/windows-smoke.ps1 -Installer <PhotoCropper-*-Setup.exe> -Samples <sample scans folder>

The sample scans come from tools/PhotoCropper.SampleScans; their expected.txt says how many photos to expect.
Every check runs even if an earlier one fails; the script exits 1 if any failed.
#>
param(
    [Parameter(Mandatory)] [string] $Installer,
    [Parameter(Mandatory)] [string] $Samples
)

$ErrorActionPreference = 'Stop'
$expected = [int]((Get-Content (Join-Path $Samples 'expected.txt') | Where-Object { $_ -like 'total *' }) -split ' ')[1]
$installDir = Join-Path ([IO.Path]::GetTempPath()) "PhotoCropperSmoke-$PID"
$results = [Collections.Generic.List[string]]::new()
$failures = 0

function Add-Result([string] $Name, [bool] $Passed) {
    $script:results.Add($(if ($Passed) { "PASS  $Name" } else { "FAIL  $Name" }))
    if (-not $Passed) { $script:failures++ }
}

# Per-user install (the installer allows it without elevation); no desktop icon, no PATH change.
$setup = Start-Process -FilePath $Installer -Wait -PassThru -ArgumentList @(
    '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', "/DIR=`"$installDir`"", '/TASKS=""')
Add-Result "installer exits cleanly (code $($setup.ExitCode))" ($setup.ExitCode -eq 0)

# --min-size 1 keeps this a packaging check whatever the CLI's default; it matches the app's own default.
$cli = Join-Path $installDir 'PhotoCropper.Cli.exe'
$out = Join-Path $installDir 'smoke-out'
if (Test-Path $cli) {
    & $cli $Samples -o $out --min-size 1 *> (Join-Path $installDir 'cli.log')
    $code = $LASTEXITCODE
    $found = @(Get-ChildItem $out -Filter *.jpg -ErrorAction SilentlyContinue).Count
    Write-Host "CLI exit code $code, $found of $expected photos"
    if ($code -ne 0 -or $found -ne $expected) { Get-Content (Join-Path $installDir 'cli.log') -Tail 25 }
    Add-Result "installed CLI finds $expected photos on the samples" ($code -eq 0 -and $found -eq $expected)
} else {
    Add-Result "installed CLI exists at $cli" $false
}

# Still running after 20 s counts as started.
$gui = Join-Path $installDir 'PhotoCropper.exe'
if (Test-Path $gui) {
    $process = Start-Process -FilePath $gui -PassThru
    Start-Sleep -Seconds 20
    $running = -not $process.HasExited
    Write-Host "GUI $(if ($running) { 'still running after 20 s' } else { "exited with code $($process.ExitCode)" })"
    if ($running) { Stop-Process -Id $process.Id -Force }
    Add-Result 'installed GUI starts' $running
} else {
    Add-Result "installed GUI exists at $gui" $false
}

$uninstaller = Get-ChildItem $installDir -Filter 'unins*.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($uninstaller) {
    $removal = Start-Process -FilePath $uninstaller.FullName -Wait -PassThru -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'
    Add-Result "uninstaller exits cleanly (code $($removal.ExitCode))" ($removal.ExitCode -eq 0)
}

Write-Host ''
Write-Host 'Windows installer smoke tests:'
$results | ForEach-Object { Write-Host "  $_" }
exit $(if ($failures -eq 0) { 0 } else { 1 })
