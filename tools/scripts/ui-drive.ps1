<#
.SYNOPSIS  Drives the real UI with Windows UI Automation: FreeField ribbon tab -> "Export Bundle" -> dialog -> Export.
           Revit must be running with a project open (and the add-in loaded). Writes the bundle into -OutFolder.
#>
param([switch]$ViaJob, [string]$Screenshot = "", [switch]$NoExport, [string]$OutFolder = (Join-Path $env:LOCALAPPDATA "FreeFieldExporter\out\ui-$(Get-Date -Format yyyyMMdd-HHmmss)"), [int]$TimeoutSec = 1800)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
function Find($root, $name, $type = $null, $scope = $TS::Descendants) {
  $c = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
  if ($type) { $c = New-Object System.Windows.Automation.AndCondition($c, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $type))) }
  $root.FindFirst($scope, $c)
}
Add-Type -Namespace FF -Name Native -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, System.UIntPtr e);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PrintWindow(System.IntPtr h, System.IntPtr hdc, uint flags);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
'@
function PhysicalClick($el) {
  $r = $el.Current.BoundingRectangle
  [FF.Native]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)) | Out-Null
  Start-Sleep -Milliseconds 150
  [FF.Native]::mouse_event(2, 0, 0, 0, [System.UIntPtr]::Zero); [FF.Native]::mouse_event(4, 0, 0, 0, [System.UIntPtr]::Zero)
}
function Invoke($el) {
  $p = $null
  if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$p)) { $p.Invoke(); return }
  if ($el.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$p)) { $p.Select(); return }
  PhysicalClick $el   # ribbon items do not expose Invoke: real mouse click at the element centre
}
$proc = Get-Process Revit | Select-Object -First 1
if ($ViaJob) {
  # Mouse clicks on the Revit ribbon are unreliable across DPI/multi-monitor setups; with a hot-reloaded build the ribbon command is the OLD staged
  # one, so open the (new) dialog through the dev job channel instead. Same dialog class and the same Export code path.
  & (Join-Path $PSScriptRoot "dev-job.ps1") -NoBuild -Mode spike -Options @{ step = "showdialog" } | Out-Null
} else {
[FF.Native]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 500
$main = $AE::FromHandle($proc.MainWindowHandle)
$tab = Find $main "FreeField" ([System.Windows.Automation.ControlType]::TabItem)
if (-not $tab) { $tab = Find $main "FreeField" }
if (-not $tab) { throw "FreeField ribbon tab not found via UI Automation" }
Invoke $tab; Start-Sleep -Milliseconds 800
$btn = Find $main "Export Bundle"
if (-not $btn) { throw "'Export Bundle' button not found" }
Invoke $btn
}
$dlg = $null
for ($i = 0; $i -lt 30 -and -not $dlg; $i++) { Start-Sleep -Seconds 1; $dlg = Find ([System.Windows.Automation.AutomationElement]::RootElement) "FreeField - Export Bundle" ([System.Windows.Automation.ControlType]::Window) }
if (-not $dlg) { throw "dialog did not appear" }
Write-Host "dialog found"
function Shot($el, $path) {
  # PrintWindow renders the window itself (independent of monitor position / DPI scaling, unlike CopyFromScreen)
  if (-not $path) { return }
  Add-Type -AssemblyName System.Drawing
  $h = [System.IntPtr]$el.Current.NativeWindowHandle
  $r = New-Object FF.Native+RECT
  [FF.Native]::GetWindowRect($h, [ref]$r) | Out-Null
  $w = $r.R - $r.L; $hh = $r.B - $r.T
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
  [FF.Native]::PrintWindow($h, $hdc, 2) | Out-Null
  $g.ReleaseHdc($hdc); $g.Dispose(); $bmp.Save($path); $bmp.Dispose()
  Write-Host "screenshot: $path (${w}x${hh})"
}
Start-Sleep -Seconds 2
Shot $dlg $Screenshot
if ($NoExport) { $c = Find $dlg "Close" ([System.Windows.Automation.ControlType]::Button); if ($c) { Invoke $c }; return }
# set output folder: the only Edit control
$edit = $dlg.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
$vp = $null
if ($edit -and $edit.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$vp)) { $vp.SetValue($OutFolder) }
$go = Find $dlg "Export" ([System.Windows.Automation.ControlType]::Button)
Invoke $go
$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
  Start-Sleep -Seconds 3
  $texts = $dlg.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name }
  $done = $texts | Where-Object { $_ -like "Done:*" -or $_ -like "Failed:*" } | Select-Object -First 1
  if ($done) { Write-Host $done; break }
}
$close = Find $dlg "Close" ([System.Windows.Automation.ControlType]::Button); if ($close) { Invoke $close }
Write-Host "output: $OutFolder"
