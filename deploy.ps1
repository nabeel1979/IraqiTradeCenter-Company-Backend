# Deploy IraqiTradeCenter Company Backend API (shared multi-tenant) to production
#
# Canonical server paths (server: 65.20.159.30):
#   Companies : D:\iraqitradecenter\api_IraqiTradeCenter_Company   (this backend API)
#               D:\iraqitradecenter\IraqiTradeCenter_Company       (company frontend)
#   Parent    : D:\iraqitradecenter\api-iraqitradecenter           (parent backend API)
#               D:\iraqitradecenter\parent.iraqitradecenter        (parent frontend)
#   Store     : D:\iraqitradecenter\IraqiTradeCenter-Store         (store frontend)
#
# This backend serves the public store endpoints (/api/store/products[/image]) that the
# store frontend reaches directly via the /capi reverse-proxy — products come straight
# from the company backend, not through the parent.
#
# Build note: pinned to .NET SDK 9 via global.json (SDK 10 Roslyn crashes on these modules).
#
# Usage: .\deploy.ps1

$ErrorActionPreference = 'Stop'

# --- Connection ---
$plink = 'E:\IraqiTradeCenter\_Tools\scripts\plink.exe'
$pscp  = 'E:\IraqiTradeCenter\_Tools\scripts\pscp.exe'
$srv   = 'gcc2026@65.20.159.30'          # public IP (use 192.168.0.50 on LAN for speed)
$pwd   = 'dRJB^ogSW%&*F6'
$hkey  = 'ssh-ed25519 255 SHA256:0VQwNgW86F3sTYHWAW0eeuwP3IvZbMq3sayljRxUhOE'

# --- Paths ---
$proj      = 'E:\IraqiTradeCenter\IraqiTradeCenter-Company-Backend\src\Host\IraqiTradeCenterCompany.API\IraqiTradeCenterCompany.API.csproj'
$buildOut  = 'E:\IraqiTradeCenter\IraqiTradeCenter-Company-Backend\src\Host\IraqiTradeCenterCompany.API\bin\Release\net8.0'
$targetDir = 'D:/iraqitradecenter/api_IraqiTradeCenter_Company'
$appPool   = 'api_IraqiTradeCenter_Company'
$apiHost   = 'api_iraqitradecenter_company.gcc.iq'   # internal host header for /capi ARR proxy

# 1) Build (SDK 9 via global.json; UseSharedCompilation=false avoids the Roslyn StackOverflow)
Write-Host "1) Building Company Backend (Release)..."
dotnet build $proj -c Release --nologo -v q /p:UseSharedCompilation=false
Write-Host "   Build complete."

# 2) Ensure hosts entry (so the store /capi ARR proxy can resolve the company API locally)
Write-Host "2) Ensuring hosts entry for $apiHost ..."
$hostsCmd = @"
`$h = "`$env:WINDIR\System32\drivers\etc\hosts"
if (-not (Select-String -Path `$h -Pattern '$apiHost' -SimpleMatch -Quiet)) {
  Add-Content -Path `$h -Value '127.0.0.1 $apiHost'
  'hosts: added'
} else { 'hosts: present' }
"@
$b64h = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($hostsCmd))
& $plink -ssh -batch -pw $pwd -hostkey $hkey $srv "powershell -NoProfile -EncodedCommand $b64h"

# 3) Stop app pool
Write-Host "3) Stopping app pool $appPool ..."
$stopCmd = "Import-Module WebAdministration; if ((Get-WebAppPoolState $appPool).Value -ne 'Stopped'){ Stop-WebAppPool $appPool }; for(`$i=0;`$i -lt 15;`$i++){ if((Get-WebAppPoolState $appPool).Value -eq 'Stopped'){break}; Start-Sleep 1 }; (Get-WebAppPoolState $appPool).Value"
$b64s = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($stopCmd))
& $plink -ssh -batch -pw $pwd -hostkey $hkey $srv "powershell -NoProfile -EncodedCommand $b64s"

# 4) Copy all company assemblies (keeps API.dll and module DLLs in version sync)
Write-Host "4) Uploading IraqiTradeCenterCompany.*.dll ..."
& $pscp -batch -pw $pwd -hostkey $hkey "$buildOut\IraqiTradeCenterCompany.*.dll" "${srv}:$targetDir/"
Write-Host "   Upload complete."

# 5) Start app pool
Write-Host "5) Starting app pool $appPool ..."
$startCmd = "Import-Module WebAdministration; Start-WebAppPool $appPool; Start-Sleep 8; (Get-WebAppPoolState $appPool).Value"
$b64st = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($startCmd))
& $plink -ssh -batch -pw $pwd -hostkey $hkey $srv "powershell -NoProfile -EncodedCommand $b64st"

Write-Host ""
Write-Host "Company backend deployed to $targetDir"
Write-Host "Verify: http://$apiHost/api/store/products?pageSize=3"
