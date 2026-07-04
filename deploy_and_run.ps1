# Auto-detect Cities: Skylines II installation
$KnownPaths = @(
    # Xbox Game Pass / Microsoft Store
    "XboxGames\Cities- Skylines II - PC Edition\Content",
    # Steam default
    "Program Files (x86)\Steam\steamapps\common\Cities Skylines II",
    # Steam libraries on other drives
    "Steam\steamapps\common\Cities Skylines II",
    "SteamLibrary\steamapps\common\Cities Skylines II"
)

$GameDir = $null
foreach ($drive in [System.IO.DriveInfo]::GetDrives() | Where-Object { $_.DriveType -eq 'Fixed' -and $_.IsReady }) {
    foreach ($path in $KnownPaths) {
        $candidate = Join-Path $drive.RootDirectory.FullName $path
        if (Test-Path (Join-Path $candidate "Cities2.exe")) {
            $GameDir = $candidate
            break
        }
    }
    if ($GameDir) { break }
}

if (-not $GameDir) {
    Write-Error "Cities: Skylines II not found. Set CITIES2_GAME_DIR environment variable."
    exit 1
}

$ScriptDir  = $PSScriptRoot
$BuildDir   = Join-Path $ScriptDir "CS2M\bin\Debug\net472"
$DistDir    = Join-Path $ScriptDir "dist\Mods\CS2M"
$TargetDir  = "$env:USERPROFILE\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\CS2M"
$GameExe    = Join-Path $GameDir "Cities2.exe"

# Close the game if running
Get-Process -Name 'Cities2' -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "Found game at: $GameDir"
Write-Host "Deploying CS2M to $TargetDir..."

If (!(Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
}

Get-ChildItem -LiteralPath $TargetDir -File -ErrorAction SilentlyContinue | Remove-Item -Force

Copy-Item (Join-Path $BuildDir 'CS2M.dll') -Destination $TargetDir -Force
Write-Host "Copied CS2M.dll"

if (Test-Path $DistDir) {
    Copy-Item "$DistDir\*" -Destination $TargetDir -Recurse -Force
    Write-Host "Copied UI assets"
} else {
    Write-Warning "UI Dist directory not found at $DistDir"
}

if (Test-Path "$BuildDir\lang") {
    Copy-Item "$BuildDir\lang" -Destination $TargetDir -Recurse -Force
    Write-Host "Copied Lang assets"
}

Write-Host "Deployment Complete."
Write-Host "Launching Game in Developer Mode..."
Start-Process $GameExe -ArgumentList "-developerMode"
