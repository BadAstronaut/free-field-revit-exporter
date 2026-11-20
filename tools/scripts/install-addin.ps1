<#
.SYNOPSIS  Builds (optional) and registers the FreeField Exporter add-in for a Revit version.
.PARAMETER RevitVersion  2025 (default), 2026 or 2027.
.PARAMETER Stage         Copy the build output to %LOCALAPPDATA%\FreeFieldExporter\addin\<ver> and point the manifest there
                         (keeps bin\ unlocked while Revit runs).
.PARAMETER NoBuild       Skip dotnet build.
Close Revit before installing (DLL locks).
#>
param([int]$RevitVersion = 2025, [switch]$Stage, [switch]$NoBuild, [string]$Configuration = "Debug")
$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$proj = Join-Path $repo "src\FreeField.Exporter.Revit\FreeField.Exporter.Revit.csproj"
if (-not $NoBuild) {
    dotnet build $proj -c $Configuration -p:Platform=x64 -p:RevitVersion=$RevitVersion --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "build failed" }
}
$outDir = Join-Path $repo "src\FreeField.Exporter.Revit\bin\x64\$Configuration"
if ($Stage) {
    $dst = Join-Path $env:LOCALAPPDATA "FreeFieldExporter\addin\$RevitVersion"
    New-Item -ItemType Directory -Force $dst | Out-Null
    Copy-Item "$outDir\*" $dst -Recurse -Force
    $outDir = $dst
}
$dll = Join-Path $outDir "FreeField.Exporter.Revit.dll"
if (-not (Test-Path $dll)) { throw "missing $dll" }
$addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
New-Item -ItemType Directory -Force $addins | Out-Null
$xml = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>FreeField Exporter</Name>
    <Assembly>$dll</Assembly>
    <FullClassName>FreeField.Exporter.Revit.App</FullClassName>
    <ClientId>7d1f3c52-8a4e-4b6f-9c0d-5e2a1b3f4d68</ClientId>
    <VendorId>FFLD</VendorId>
    <VendorDescription>FreeField</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
$target = Join-Path $addins "FreeField.Exporter.addin"
Set-Content -Path $target -Value $xml -Encoding UTF8
Write-Host "Installed: $target -> $dll"

# Optional: sign the DLLs so Revit does not prompt about an unsigned add-in (only if the dev cert already exists).
$signer = Join-Path $PSScriptRoot "..\signing\sign-addins.ps1"
if ((Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue | Where-Object { $_.Subject -eq "CN=FreeField Exporter Dev" }) -and (Test-Path $signer)) {
    & $signer -RevitVersion $RevitVersion
} else {
    Write-Host "Hint: run tools\signing\sign-addins.ps1 -Setup once to avoid Revit's unsigned add-in prompt."
}
