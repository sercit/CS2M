$GameDir = "g:\Games\Cities.Skylines.II.v1.5.3f1\Cities.Skylines.II.v1.5.3f1\game"
$BuildDir = "$GameDir\CS2M\CS2M\bin\Debug\net472"
$DistDir = "$GameDir\CS2M\dist\Mods\CS2M"
$TargetDir = "$env:USERPROFILE\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\CS2M"
$GameExe = "$GameDir\Cities2.exe"

Write-Host "Deploying CS2M to $TargetDir..."

# Create target dir
If (!(Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
}

# Wipe stale deploy (PDB, IDB, cached files from prior broken builds)
Get-ChildItem -LiteralPath $TargetDir -File -ErrorAction SilentlyContinue | Remove-Item -Force

# Ship ONLY the ILRepack-merged CS2M.dll. CS2M.API.dll and CS2M.BaseGame.dll
# are also inputs to ILRepack (see CS2M.csproj <PackAssemblies>) and their
# types end up inside CS2M.dll. The game's mod manager scans every DLL in
# the mod folder; if it finds CS2M.API.dll / CS2M.BaseGame.dll on their own
# (no IMod implementation), it creates a ModInfo with a null assembly and
# later throws NullReferenceException at ModInfo.get_assemblyFullName,
# which aborts InitializeMods and leaves the UI bindings unregistered.
# The same hazard applies to the satellite DLLs (LiteNetLib, MessagePack,
# 0Harmony, System.*) which is why those are not in this folder either.
Copy-Item (Join-Path $BuildDir 'CS2M.dll') -Destination $TargetDir -Force
Write-Host "Copied CS2M.dll"

# Copy UI
if (Test-Path $DistDir) {
    Copy-Item "$DistDir\*" -Destination $TargetDir -Recurse -Force
    Write-Host "Copied UI assets"
}
else {
    Write-Warning "UI Dist directory not found at $DistDir"
}

# Copy Lang
if (Test-Path "$BuildDir\lang") {
    Copy-Item "$BuildDir\lang" -Destination $TargetDir -Recurse -Force
    Write-Host "Copied Lang assets"
}

Write-Host "Deployment Complete."

# Launch Game
Write-Host "Launching Game in Developer Mode..."
Start-Process $GameExe -ArgumentList "-developerMode"
