<#
.SYNOPSIS  Drives the real UI with Windows UI Automation: FreeField ribbon tab -> "Export Bundle" -> dialog -> Export.
           Revit must be running with a project open (and the add-in loaded). Writes the bundle into -OutFolder.
#>
param([string]$OutFolder = (Join-Path $env:LOCALAPPDATA "FreeFieldExporter\out\ui-$(Get-Date -Format yyyyMMdd-HHmmss)"), [int]$TimeoutSec = 1800)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
function Find($root, $name, $type = $null, $scope = $TS::Descendants) {
  $c = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
  if ($type) { $c = New-Object System.Windows.Automation.AndCondition($c, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $type))) }
  $root.FindFirst($scope, $c)
}
function Invoke($el) {
  $p = $null
  if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$p)) { $p.Invoke(); return }
  if ($el.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$p)) { $p.Select(); return }
  throw "no invoke pattern on $($el.Current.Name)"
}
$proc = Get-Process Revit | Select-Object -First 1
$main = $AE::FromHandle($proc.MainWindowHandle)
$tab = Find $main "FreeField" ([System.Windows.Automation.ControlType]::TabItem)
if (-not $tab) { $tab = Find $main "FreeField" }
if (-not $tab) { throw "FreeField ribbon tab not found via UI Automation" }
Invoke $tab; Start-Sleep -Milliseconds 800
$btn = Find $main "Export Bundle"
if (-not $btn) { throw "'Export Bundle' button not found" }
Invoke $btn
$dlg = $null
for ($i = 0; $i -lt 30 -and -not $dlg; $i++) { Start-Sleep -Seconds 1; $dlg = Find ([System.Windows.Automation.AutomationElement]::RootElement) "FreeField - Export Bundle" ([System.Windows.Automation.ControlType]::Window) }
if (-not $dlg) { throw "dialog did not appear" }
Write-Host "dialog found"
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
