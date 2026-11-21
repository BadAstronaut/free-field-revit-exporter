<#
.SYNOPSIS  Dev loop against an ALREADY RUNNING Revit with the add-in loaded: rebuild (dev-build.ps1), drop a job into
           %LOCALAPPDATA%\FreeFieldExporter\jobs, wait for the result file.
#>
param([string]$Mode = "spike", [hashtable]$Options = @{}, [string]$Rvt, [string]$Output = (Join-Path $env:LOCALAPPDATA "FreeFieldExporter\out\spike"),
      [string[]]$Sheets, [switch]$NoBuild, [switch]$ExitWhenDone, [int]$TimeoutSec = 900, [int]$RevitVersion = 2025)
$ErrorActionPreference = "Stop"
if (-not $NoBuild) { & (Join-Path $PSScriptRoot "dev-build.ps1") -RevitVersion $RevitVersion | Select-Object -Last 1 }
$root = Join-Path $env:LOCALAPPDATA "FreeFieldExporter"
$jobs = Join-Path $root "jobs"; New-Item -ItemType Directory -Force $jobs | Out-Null
$stamp = Get-Date -Format "HHmmss"
$res = Join-Path $root "jobs-run\result-$stamp.json"; New-Item -ItemType Directory -Force (Split-Path $res) | Out-Null
$obj = [ordered]@{ mode = $Mode; rvt = $Rvt; output = $Output; sheets = $(if ($Sheets) { $Sheets } else { $null }); exitWhenDone = $ExitWhenDone.IsPresent; resultFile = $res; options = $Options }
$tmp = Join-Path $jobs "job-$stamp.tmp"
$obj | ConvertTo-Json -Depth 5 | Set-Content $tmp -Encoding UTF8
Rename-Item $tmp "job-$stamp.json"
$sw = [Diagnostics.Stopwatch]::StartNew()
while (-not (Test-Path $res) -and $sw.Elapsed.TotalSeconds -lt $TimeoutSec) { Start-Sleep -Milliseconds 700 }
if (Test-Path $res) {
  $r = Get-Content $res -Raw | ConvertFrom-Json
  Write-Host ("status={0} totalMs={1} dialogs={2}" -f $r.status, $r.totalMs, @($r.dialogs).Count)
  $r.PSObject.Properties | Where-Object { $_.Name -like "error*" } | ForEach-Object { Write-Host ($_.Name + ": " + $_.Value.ToString().Substring(0, [Math]::Min(1200, $_.Value.ToString().Length))) }
} else { Write-Host "TIMEOUT (is Revit running and idle? Idling may be throttled when Revit is not focused)" }
