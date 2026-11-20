param([int]$RevitVersion = 2025)
$t = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion\FreeField.Exporter.addin"
if (Test-Path $t) { Remove-Item $t -Force; Write-Host "Removed $t" } else { Write-Host "Not installed" }
