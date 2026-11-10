# Publishes a self-contained Windows build and (if Inno Setup is installed) compiles the installer.
# Usage: pwsh scripts/publish.ps1 [-Version 1.0.0]
param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts/publish"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish (Join-Path $root "src/DualSenseUltimate.App/DualSenseUltimate.App.csproj") `
  -c Release -r win-x64 --self-contained true -p:Version=$Version -p:DebugType=none -o $out
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# Guided hardware test, published into the same folder so it shares the bundled .NET runtime.
dotnet publish (Join-Path $root "tools/HardwareCheck/HardwareCheck.csproj") `
  -c Release -r win-x64 --self-contained true -p:Version=$Version -p:DebugType=none -o $out
if ($LASTEXITCODE -ne 0) { throw "hardware check publish failed" }

# Bundle the sample plugins as disabled, ready-to-copy examples.
foreach ($p in "BatteryLoggerPlugin", "HomeAssistantPlugin") {
  dotnet publish (Join-Path $root "samples/plugins/$p/$p.csproj") -c Release -o (Join-Path $out "sample-plugins/$p")
  if ($LASTEXITCODE -ne 0) { throw "plugin publish failed" }
}

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
  & $iscc "/DAppVersion=$Version" (Join-Path $root "installer/DualSenseUltimate.iss")
  if ($LASTEXITCODE -ne 0) { throw "installer build failed" }
  Write-Host "Installer: installer/Output/DualSenseUltimate-Setup-$Version.exe"
} else {
  Write-Warning "Inno Setup 6 not found: the app was published to $out but no installer was built."
}
