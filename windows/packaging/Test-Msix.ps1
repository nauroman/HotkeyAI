<#
.SYNOPSIS
  Installs the CI test package on a disposable GitHub-hosted runner, launches it from Start,
  checks it runs from the package with the welcome window and startup left off, then removes it.
  It trusts a temporary self-signed certificate, so it refuses to run anywhere else.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Package)

$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Run this installation test on a disposable GitHub-hosted Windows runner.'
}
$Package = (Resolve-Path $Package).Path

$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=HotkeyAI.CI' -CertStoreLocation Cert:\CurrentUser\My
$public = Join-Path $env:RUNNER_TEMP 'HotkeyAI.CI.cer'
Export-Certificate -Cert $cert -FilePath $public | Out-Null
Import-Certificate -FilePath $public -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
$signed = Join-Path $env:RUNNER_TEMP (Split-Path $Package -Leaf)
Copy-Item -LiteralPath $Package -Destination $signed
$bin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$signtool = Get-ChildItem $bin -Directory | Where-Object Name -match '^10\.' | Sort-Object { [version]$_.Name } -Descending |
    ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } | Where-Object { Test-Path $_ } | Select-Object -First 1
& $signtool sign /fd SHA256 /sha1 $cert.Thumbprint $signed
if ($LASTEXITCODE) { throw 'Signing the test package failed' }

Add-AppxPackage -Path $signed
$app = Get-AppxPackage -Name HotkeyAI.CI
if (!$app) { throw 'Package registration failed' }
try {
    Start-Process explorer.exe -ArgumentList ("shell:AppsFolder\{0}!HotkeyAI" -f $app.PackageFamilyName)
    $proc = $null
    for ($i = 0; $i -lt 60 -and !$proc; $i++) {
        Start-Sleep -Milliseconds 500
        $proc = Get-Process HotkeyAI -ErrorAction SilentlyContinue | Where-Object MainWindowTitle -eq 'Welcome to HotkeyAI'
    }
    if (!$proc) { throw 'The packaged app did not show its welcome window' }
    if ($proc.Path -ne (Join-Path $app.InstallLocation 'HotkeyAI.exe')) { throw "Unexpected process path $($proc.Path)" }
    Write-Host 'ok: launched from Start, welcome window shown'

    $task = "HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\$($app.PackageFamilyName)\HotkeyAIStartup"
    if ((Test-Path -LiteralPath $task) -and (Get-ItemPropertyValue -LiteralPath $task -Name State -ErrorAction SilentlyContinue) -eq 2) {
        throw 'Startup was turned on without the user asking'
    }
    Write-Host 'ok: open-at-sign-in is off until the user turns it on'

    # A second launch must not start a second copy.
    Start-Process explorer.exe -ArgumentList ("shell:AppsFolder\{0}!HotkeyAI" -f $app.PackageFamilyName)
    Start-Sleep -Seconds 4
    $count = @(Get-Process HotkeyAI -ErrorAction SilentlyContinue).Count
    if ($count -ne 1) { throw "Expected one HotkeyAI process after a second launch, found $count" }
    Write-Host 'ok: a second launch reuses the running copy'
} finally {
    Get-Process HotkeyAI -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-AppxPackage -Package $app.PackageFullName
}
if (Get-AppxPackage -Name HotkeyAI.CI) { throw 'Package survived uninstall' }
Write-Host 'ok: uninstalled cleanly'
