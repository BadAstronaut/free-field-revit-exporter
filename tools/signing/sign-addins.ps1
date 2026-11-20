<#
.SYNOPSIS
  Sign Revit add-in DLLs with a local, trusted code-signing certificate so Revit loads them
  without the "Unsigned add-in / Load once / Always load" security prompt.

.DESCRIPTION
  -Setup   (once per machine/user, then also signs the DLLs) creates a self-signed code-signing certificate
           "CN=FreeField Exporter Dev" in Cert:\CurrentUser\My (private key not exportable) and
           trusts its public part in CurrentUser\TrustedPublisher and CurrentUser\Root.
           Windows shows ONE confirmation dialog for the Root import: click "Yes".
  default  signs the given DLLs (Revit must be closed: loaded DLLs are locked).
  -Remove  deletes the certificate from all three stores (undo).

  Revit verifies the Authenticode signature of the assembly named in each .addin manifest;
  a valid signature from a trusted publisher loads silently. Re-run signing after every build.

.EXAMPLE
  ./sign-addins.ps1 -Setup
  ./sign-addins.ps1 -Paths "$env:LOCALAPPDATA\FreeFieldExporter\addin\2025\FreeField.Exporter.Revit.dll"
  ./sign-addins.ps1            # signs the default FreeField + Revit MCP DLLs for -RevitVersion (2025)
#>
[CmdletBinding()]
param(
  [switch]$Setup,
  [switch]$Remove,
  [string[]]$Paths,
  [string]$RevitVersion = "2025"
)
$ErrorActionPreference = "Stop"
$Subject = "CN=FreeField Exporter Dev"

function Get-SigningCert {
  Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object { $_.Subject -eq $Subject -and $_.NotAfter -gt (Get-Date) } |
    Sort-Object NotAfter -Descending | Select-Object -First 1
}

if ($Remove) {
  foreach ($store in "My", "TrustedPublisher", "Root") {
    Get-ChildItem "Cert:\CurrentUser\$store" | Where-Object { $_.Subject -eq $Subject } |
      ForEach-Object { Remove-Item $_.PSPath -Confirm:$false; "removed from $store : $($_.Thumbprint)" }
  }
  return
}

if ($Setup) {
  $cert = Get-SigningCert
  if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Subject `
      -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy NonExportable `
      -KeyUsage DigitalSignature -NotAfter (Get-Date).AddYears(5)
    "created certificate $($cert.Thumbprint)"
  } else { "certificate exists $($cert.Thumbprint)" }
  $cer = Join-Path $env:TEMP "freefield-exporter-dev.cer"
  Export-Certificate -Cert $cert -FilePath $cer | Out-Null
  foreach ($store in "TrustedPublisher", "Root") {
    if (-not (Get-ChildItem "Cert:\CurrentUser\$store" | Where-Object Thumbprint -eq $cert.Thumbprint)) {
      Import-Certificate -FilePath $cer -CertStoreLocation "Cert:\CurrentUser\$store" | Out-Null
      "trusted in $store"
    } else { "already trusted in $store" }
  }
  Remove-Item $cer -ErrorAction SilentlyContinue
  # fall through: sign the default DLLs right away so one command fixes the prompt
}

$cert = Get-SigningCert
if (-not $cert) { throw "No signing certificate. Run: ./sign-addins.ps1 -Setup" }

if (-not $Paths) {
  $Paths = @(
    (Join-Path $env:LOCALAPPDATA "FreeFieldExporter\addin\$RevitVersion\FreeField.Exporter.Revit.dll"),
    (Join-Path $env:LOCALAPPDATA "FreeFieldExporter\addin\$RevitVersion\FreeField.Exporter.Core.dll"),
    (Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion\RevitMCPAddin.dll"),
    (Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion\RevitMCP.Core.dll")
  ) | Where-Object { Test-Path $_ }
}

$failed = 0
foreach ($p in $Paths) {
  $sig = Set-AuthenticodeSignature -FilePath $p -Certificate $cert -HashAlgorithm SHA256
  "{0,-8} {1}" -f $sig.Status, $p
  if ($sig.Status -ne "Valid") { $failed++ }
}
if ($failed) { throw "$failed file(s) not validly signed (is Revit running and locking them?)" }
