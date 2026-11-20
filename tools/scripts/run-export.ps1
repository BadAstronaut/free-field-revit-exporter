<#
.SYNOPSIS  Headless export: writes a job, launches Revit with FREEFIELD_EXPORT_JOB (this process tree only),
           waits for resultFile, prints a compact summary.
.PARAMETER Rvt     work-copy .rvt (never the original)
.PARAMETER Output  output .ffbundle path (or folder)
.PARAMETER Mode    export (default) | spike
.PARAMETER Sheets  optional sheet numbers
.PARAMETER KeepOpen  do not exit Revit when done
#>
param(
  [Parameter(Mandatory)][string]$Rvt,
  [string]$Output,
  [int]$RevitVersion = 2025,
  [string]$Mode = "export",
  [string[]]$Sheets,
  [hashtable]$Options = @{},
  [switch]$KeepOpen,
  [int]$TimeoutSec = 1800
)
$ErrorActionPreference = "Stop"
$work = Join-Path $env:LOCALAPPDATA "FreeFieldExporter\jobs-run"
New-Item -ItemType Directory -Force $work | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
# Never reuse or delete earlier outputs: default to a fresh timestamped folder; an explicit -Output is a folder that is only added to.
if (-not $Output) { $Output = Join-Path (Join-Path $env:LOCALAPPDATA "FreeFieldExporter\out") "run-$stamp" }
$job = Join-Path $work "job-$stamp.json"
$res = Join-Path $work "result-$stamp.json"
$obj = [ordered]@{ mode = $Mode; rvt = (Resolve-Path $Rvt).Path; output = $Output; sheets = $(if ($Sheets) { $Sheets } else { $null });
  exitWhenDone = (-not $KeepOpen.IsPresent); resultFile = $res; options = $Options }
$obj | ConvertTo-Json -Depth 5 | Set-Content $job -Encoding UTF8
$exe = "C:\Program Files\Autodesk\Revit $RevitVersion\Revit.exe"
if (Get-Process Revit -ErrorAction SilentlyContinue) { throw "Revit already running; close it or drop the job in %LOCALAPPDATA%\FreeFieldExporter\jobs (dev mode)." }
$psi = New-Object System.Diagnostics.ProcessStartInfo $exe
$psi.UseShellExecute = $false
$psi.EnvironmentVariables["FREEFIELD_EXPORT_JOB"] = $job
$p = [System.Diagnostics.Process]::Start($psi)
Write-Host "Revit pid $($p.Id); job $job"
$sw = [Diagnostics.Stopwatch]::StartNew()
while (-not (Test-Path $res) -and $sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
  if ($p.HasExited) { Write-Host "Revit exited before result"; break }
  Start-Sleep -Seconds 2
}
if (Test-Path $res) {
  $r = Get-Content $res -Raw | ConvertFrom-Json
  Write-Host ("status={0} totalMs={1} dialogs={2}" -f $r.status, $r.totalMs, @($r.dialogs).Count)
  if ($r.error) { Write-Host ($r.error.ToString().Substring(0, [Math]::Min(800, $r.error.ToString().Length))) }
  Write-Host "result: $res"
} else { Write-Host "TIMEOUT/no result" }
if (-not $KeepOpen) {
  if (-not $p.WaitForExit(60000)) { Write-Host "Revit still running after exit request (killing; work copy only)"; $p.Kill() }
}
