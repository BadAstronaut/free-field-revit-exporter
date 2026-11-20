<#
.SYNOPSIS  Builds the add-in into a fresh dev folder and points hot.json at it, so a RUNNING Revit picks up the new
           worker code on the next job without restart (see App.cs). Prints the dll path.
#>
param([int]$RevitVersion = 2025)
$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$proj = Join-Path $repo "src\FreeField.Exporter.Revit\FreeField.Exporter.Revit.csproj"
$dev = Join-Path $env:LOCALAPPDATA "FreeFieldExporter\dev"
$out = Join-Path $dev ("build-" + (Get-Date -Format "HHmmss"))
dotnet build $proj -c Debug -p:Platform=x64 -p:RevitVersion=$RevitVersion -o $out --nologo -v q 2>&1 | Select-String -Pattern "error|Warn.*FF" | Select-Object -First 20
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$dll = Join-Path $out "FreeField.Exporter.Revit.dll"
@{ dll = $dll } | ConvertTo-Json | Set-Content (Join-Path $dev "hot.json") -Encoding UTF8
Write-Host $dll
