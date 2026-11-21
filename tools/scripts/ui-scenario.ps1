<#
.SYNOPSIS  Launch-readiness UI scenario driven through Windows UI Automation (Revit running with a project open):
           open the dialog, screenshot, expand "Other sheets (not mapped)" and the Levels panel (screenshots), tick one non-mappable sheet,
           confirm the summary changes, untick it, Export, wait for the result panel, screenshot it.
           The dialog is opened through the dev job channel (step "showdialog"): same dialog class and Export code path as the ribbon button.
#>
param([string]$ShotDir = (Join-Path $env:LOCALAPPDATA "FreeFieldExporter\out\ui-shots"), [string]$OutFolder = (Join-Path $env:LOCALAPPDATA "FreeFieldExporter\out\ui-$(Get-Date -Format yyyyMMdd-HHmmss)"), [int]$TimeoutSec = 2400)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type -Namespace FF2 -Name Native -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PrintWindow(System.IntPtr h, System.IntPtr hdc, uint flags);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
'@
New-Item -ItemType Directory -Force $ShotDir | Out-Null
$AE = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]; $CT = [System.Windows.Automation.ControlType]
function Cond($prop, $val) { New-Object System.Windows.Automation.PropertyCondition($prop, $val) }
function FindByName($root, $name, $type = $null) { $c = Cond $AE::NameProperty $name; if ($type) { $c = New-Object System.Windows.Automation.AndCondition($c, (Cond $AE::ControlTypeProperty $type)) }; $root.FindFirst($TS::Descendants, $c) }
function Shot($win, $file) {
  $h = [System.IntPtr]$win.Current.NativeWindowHandle; $r = New-Object FF2.Native+RECT; [FF2.Native]::GetWindowRect($h, [ref]$r) | Out-Null
  $bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T); $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
  [FF2.Native]::PrintWindow($h, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc); $g.Dispose(); $p = Join-Path $ShotDir $file; $bmp.Save($p); $bmp.Dispose(); Write-Host "screenshot: $p"
}
function TextNow($win, $regex) { try { foreach ($t in $win.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Text))) { if ($t.Current.Name -match $regex) { return $t.Current.Name } }; return $null } catch { return $null } }   # UIA times out while Revit is busy exporting: caller retries
function Expand($el, $open) { $p = $el.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern); if ($open) { $p.Expand() } else { $p.Collapse() }; Start-Sleep -Milliseconds 700 }
function Press($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }

& (Join-Path $PSScriptRoot "dev-job.ps1") -NoBuild -Mode spike -Options @{ step = "showdialog" } | Out-Null
$dlg = $null
for ($i = 0; $i -lt 60 -and -not $dlg; $i++) { Start-Sleep -Seconds 1; $dlg = FindByName $AE::RootElement "FreeField - Export Bundle" $CT::Window }
if (-not $dlg) { throw "dialog did not appear" }
Start-Sleep -Seconds 2
Shot $dlg "01-dialog-default.png"
$sum0 = TextNow $dlg "^[0-9]+ sheets .* levels"; Write-Host "summary (default): $sum0"

# other sheets group
$other = FindByName $dlg "Other sheets (not mapped)"
Expand $other $true; Shot $dlg "02-other-sheets-expanded.png"
# tick the first non-mappable sheet, confirm summary changes
$grids = $dlg.FindAll($TS::Descendants, (Cond $AE::NameProperty "Sheets"))
$otherGrid = $other.FindFirst($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::DataGrid))
$cb = $otherGrid.FindFirst($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::CheckBox))
$tog = $cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
$tog.Toggle(); Start-Sleep -Milliseconds 800
$sum1 = TextNow $dlg "^[0-9]+ sheets .* levels"; Write-Host "summary (one non-mappable sheet ticked): $sum1"
Shot $dlg "03-non-mappable-ticked.png"
if ($sum1 -eq $sum0) { throw "summary did not change after ticking a non-mappable sheet" }
$tog.Toggle(); Start-Sleep -Milliseconds 800
$sum2 = TextNow $dlg "^[0-9]+ sheets .* levels"; Write-Host "summary (unticked again): $sum2"
if ($sum2 -ne $sum0) { throw "summary did not return to the default" }
Expand $other $false

# levels panel
$lv = FindByName $dlg "Levels panel"; Expand $lv $true; Shot $dlg "04-levels-panel.png"; Expand $lv $false

# export
$edit = $dlg.FindFirst($TS::Descendants, (Cond $AE::NameProperty "Output folder"))
$edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($OutFolder)
Press (FindByName $dlg "Export" $CT::Button)
$sw = [Diagnostics.Stopwatch]::StartNew(); $res = $null
while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec -and -not $res) { Start-Sleep -Seconds 3; $res = TextNow $dlg "^\d+ pages \(" ; if (-not $res) { $f = TextNow $dlg "^Failed:"; if ($f) { throw $f } } }
if (-not $res) { throw "export did not finish in time" }
Write-Host ("result panel after {0:N0}s: {1}" -f $sw.Elapsed.TotalSeconds, $res)
Shot $dlg "05-result-panel.png"
Press (FindByName $dlg "Close" $CT::Button)
Write-Host "output: $OutFolder"
