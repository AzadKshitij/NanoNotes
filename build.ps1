param([string]$Configuration = "Debug")

$ErrorActionPreference = "Stop"

# Repo root (this script's folder) holds the solution; the project lives in notes\.
# Generated files stay beside the repo, not inside it: layout\ and packaging\ are
# siblings of the repo folder.
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoParent = Split-Path -Parent $root
$projectDir = Join-Path $root "notes"
$projectFile = Join-Path $projectDir "notes.csproj"
$layout = Join-Path $repoParent "layout"
$manifestSource = Join-Path $repoParent "packaging\AppxManifest.xml"

if (-not (Test-Path $manifestSource)) {
    throw "Manifest not found at $manifestSource. packaging\AppxManifest.xml must sit next to the repo folder."
}

$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

# The Windows SDK is not installed on this machine, so point C#/WinRT at the
# Windows metadata that ships with the Microsoft.Windows.SDK.NET.Ref package.
$sdkVersion = [regex]::Match(
    (Get-Content $projectFile -Raw),
    '<WindowsSdkPackageVersion>([^<]+)<'
).Groups[1].Value
if (-not $sdkVersion) { throw "Could not read WindowsSdkPackageVersion from $projectFile" }

$winmd = Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windows.sdk.net.ref\$sdkVersion\winmd"
if (-not (Test-Path $winmd)) {
    throw "Windows metadata not found at $winmd. Run 'dotnet restore' in $projectDir once."
}

# The palette keeps the extension exe loaded, which locks the output files.
Get-Process notes -ErrorAction SilentlyContinue | Stop-Process -Force

& $dotnet build $projectFile -c $Configuration -r win-x64 --self-contained `
    -p:Platform=x64 -p:CsWinRTWindowsMetadata="$winmd"
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$exe = Get-ChildItem (Join-Path $projectDir "bin\x64\$Configuration") -Recurse -Filter notes.exe |
    Select-Object -First 1
if (-not $exe) { throw "notes.exe was not produced." }
$out = $exe.DirectoryName

New-Item -ItemType Directory -Force $layout | Out-Null
Copy-Item "$out\*" $layout -Recurse -Force
# Copy the Assets tree as exactly layout\Assets. Reusing a persistent layout,
# Copy-Item would nest it as layout\Assets\Assets and leave stale icons behind.
$layoutAssets = Join-Path $layout "Assets"
if (Test-Path $layoutAssets) { Remove-Item $layoutAssets -Recurse -Force }
Copy-Item (Join-Path $projectDir "Assets") $layoutAssets -Recurse -Force
New-Item -ItemType Directory -Force (Join-Path $layout "Public") | Out-Null
Copy-Item $manifestSource (Join-Path $layout "AppxManifest.xml") -Force

# Without a PRI file the manifest needs the actual (unqualified) image names.
$assets = Join-Path $layout "Assets"
@(
    @("SplashScreen.scale-200.png", "SplashScreen.png"),
    @("Square150x150Logo.scale-200.png", "Square150x150Logo.png"),
    @("Square44x44Logo.scale-200.png", "Square44x44Logo.png"),
    @("Wide310x150Logo.scale-200.png", "Wide310x150Logo.png")
) | ForEach-Object {
    Copy-Item (Join-Path $assets $_[0]) (Join-Path $assets $_[1]) -Force
}

$manifest = Join-Path $layout "AppxManifest.xml"

# The old local-test identity ("notes") shared the same COM class ID as the
# Store identity, so it must go before the new package can register.
Get-AppxPackage notes -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
Get-AppxPackage AzadKshitij.NanoNotes -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue

try {
    Add-AppxPackage -Register $manifest -ErrorAction Stop
} catch {
    Add-AppxPackage -Register $manifest -ErrorAction Stop
}

# The palette keeps COM references to the extension process it started. Once a
# rebuild replaces that process those references go stale and every list call
# fails with "Unexpected HRESULT ... 0x800706BA". Restarting the palette makes it
# pick up the new package and a fresh server.
Get-Process Microsoft.CmdPal.UI -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
Start-Process "explorer.exe" "shell:AppsFolder\Microsoft.CommandPalette_8wekyb3d8bbwe!App"
Start-Sleep -Milliseconds 1500

Write-Host ""
Write-Host "Built, registered, palette restarted. Press Alt+Space and type 'notes'." -ForegroundColor Green
