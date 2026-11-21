<#
.SYNOPSIS  Final-launch UI run against the INSTALLED add-in: click the real FreeField ribbon button through UI Automation, screenshot the
           dialog, Export with the default selection, wait for the result panel, screenshot it, close the dialog.
           Ribbon control order tried: InvokePattern / SelectionItemPattern, then keyboard focus + Enter, then a physical click (DPI-aware).
#>
param([switch]$DialogOpen, [Parameter(Mandatory)][string]$OutDir, [string]$Label = "run", [int]$TimeoutSec = 2400)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type -Namespace FF4 -Name Native -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PrintWindow(System.IntPtr h, System.IntPtr hdc, uint flags);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, System.UIntPtr e);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
'@
New-Item -ItemType Directory -Force "$OutDir\screens" | Out-Null
[FF4.Native]::SetProcessDPIAware() | Out-Null   # physical pixel coordinates for UIA rectangles and mouse_event
$AE = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]; $CT = [System.Windows.Automation.ControlType]
function Cond($prop, $val) { New-Object System.Windows.Automation.PropertyCondition($prop, $val) }
function FindByName($root, $name, $type = $null) { $c = Cond $AE::NameProperty $name; if ($type) { $c = New-Object System.Windows.Automation.AndCondition($c, (Cond $AE::ControlTypeProperty $type)) }; $root.FindFirst($TS::Descendants, $c) }
function Shot($win, $file) {
  $h = [System.IntPtr]$win.Current.NativeWindowHandle; $r = New-Object FF4.Native+RECT; [FF4.Native]::GetWindowRect($h, [ref]$r) | Out-Null
  $bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T); $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
  [FF4.Native]::PrintWindow($h, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc); $g.Dispose(); $p = Join-Path "$OutDir\screens" $file; $bmp.Save($p); $bmp.Dispose(); Write-Host "screenshot: $p"
}
function TextNow($win, $regex) { try { foreach ($t in $win.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Text))) { if ($t.Current.Name -match $regex) { return $t.Current.Name } }; return $null } catch { return $null } }
function Activate($el, $what) {
  $p = $null
  if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$p)) { $p.Invoke(); return "InvokePattern" }
  if ($el.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$p)) { $p.Select(); return "SelectionItemPattern" }
  try { $el.SetFocus(); Start-Sleep -Milliseconds 300; [System.Windows.Forms.SendKeys]::SendWait("{ENTER}"); Start-Sleep -Milliseconds 1500; if ($script:ProbeDialog.Invoke()) { return "keyboard focus + Enter" } } catch { }
  $r = $el.Current.BoundingRectangle
  [FF4.Native]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)) | Out-Null; Start-Sleep -Milliseconds 150
  [FF4.Native]::mouse_event(2, 0, 0, 0, [System.UIntPtr]::Zero); [FF4.Native]::mouse_event(4, 0, 0, 0, [System.UIntPtr]::Zero); return "physical click"
}
$script:ProbeDialog = { $null -ne (FindByName $AE::RootElement "FreeField - Export Bundle" $CT::Window) }
$proc = Get-Process Revit | Select-Object -First 1
[FF4.Native]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null; Start-Sleep -Milliseconds 500
if (-not $DialogOpen) {
$main = $AE::FromHandle($proc.MainWindowHandle)
$tab = FindByName $main "FreeField" $CT::TabItem; if (-not $tab) { $tab = FindByName $main "FreeField" }
if (-not $tab) { throw "FreeField ribbon tab not found via UI Automation" }
Write-Host ("ribbon tab activated with: " + (Activate $tab "tab")); Start-Sleep -Milliseconds 1200
$btn = FindByName $main "Export Bundle"; if (-not $btn) { throw "'Export Bundle' button not found" }
Write-Host ("ribbon button activated with: " + (Activate $btn "button"))
}
$dlg = $null; for ($i = 0; $i -lt 90 -and -not $dlg; $i++) { Start-Sleep -Seconds 1; $dlg = FindByName $AE::RootElement "FreeField - Export Bundle" $CT::Window }
if (-not $dlg) { throw "dialog did not appear after clicking the ribbon button" }
Start-Sleep -Seconds 2
Shot $dlg "$Label-01-dialog-default.png"
$sum = TextNow $dlg "^[0-9]+ sheets .* levels"; Write-Host "summary: $sum"
$exp = FindByName $dlg "Export" $CT::Button; Write-Host ("Export enabled: " + $exp.Current.IsEnabled)
if (-not $exp.Current.IsEnabled) { Write-Host "Export disabled (nothing selected)"; $c = FindByName $dlg "Close" $CT::Button; $c.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); return }
$edit = $dlg.FindFirst($TS::Descendants, (Cond $AE::NameProperty "Output folder"))
$edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("$OutDir\bundle")
$exp.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
$sw = [Diagnostics.Stopwatch]::StartNew(); $res = $null
while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec -and -not $res) { Start-Sleep -Seconds 4; $res = TextNow $dlg "^[0-9]+ pages \("; if (-not $res) { $f = TextNow $dlg "^Failed:"; if ($f) { Shot $dlg "$Label-02-failed.png"; throw $f } } }
if (-not $res) { throw "export did not finish in time" }
Write-Host ("result panel after {0:N0}s: {1}" -f $sw.Elapsed.TotalSeconds, $res)
Shot $dlg "$Label-02-result-panel.png"
$c = FindByName $dlg "Close" $CT::Button; $c.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Host "bundle folder: $OutDir\bundle"
