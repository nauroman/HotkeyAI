<#
.SYNOPSIS
  Builds the HotkeyAI MSIX packages (x64 and arm64) and a bundle for the Microsoft Store.

.DESCRIPTION
  Needs Windows with the .NET 10 SDK and the Windows 10/11 SDK (MakeAppx, MakePri).
  -Store uses the Partner Center identity in StoreIdentity.json (Product management >
  Product identity). Store packages are unsigned: Microsoft signs them after certification.
  Without -Store the script builds a test package with a CI identity.

.EXAMPLE
  .\windows\packaging\Build-Msix.ps1 -Store -OutputDirectory build\store
#>
[CmdletBinding()]
param(
    [switch]$Store,
    [string]$IdentityName = 'HotkeyAI.CI',
    [string]$Publisher = 'CN=HotkeyAI.CI',
    [string]$PublisherDisplayName = 'HotkeyAI CI',
    [string]$Version,
    [string[]]$Architectures = @('x64', 'arm64'),
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
if ($Store) {
    $identity = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'StoreIdentity.json') -Raw | ConvertFrom-Json
    $IdentityName = $identity.IdentityName
    $Publisher = $identity.Publisher
    $PublisherDisplayName = $identity.PublisherDisplayName
}
$windows = Split-Path $PSScriptRoot -Parent
$root = Split-Path $windows -Parent
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root 'build\msix' }

if (!$Version) {
    [xml]$props = Get-Content -LiteralPath (Join-Path $windows 'Directory.Build.props') -Raw
    $Version = "$($props.Project.PropertyGroup.Version).0"
}
if (([version]$Version).Revision -ne 0) {
    throw 'The Store needs a four-part version ending in .0, for example 1.0.0.0.'
}

function Find-SdkTool([string]$name) {
    $bin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $tool = Get-ChildItem -LiteralPath $bin -Directory | Where-Object Name -match '^10\.0\.\d+\.\d+$' |
        Sort-Object { [version]$_.Name } -Descending | ForEach-Object { Join-Path $_.FullName "x64\$name" } |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (!$tool) { throw "Install the Windows 10 or 11 SDK ($name not found)." }
    return $tool
}

$makeAppx = Find-SdkTool 'makeappx.exe'
$makePri = Find-SdkTool 'makepri.exe'
$work = Join-Path $root 'build\msix-work'
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work, $OutputDirectory | Out-Null
$bundleInput = New-Item -ItemType Directory -Force -Path (Join-Path $work 'bundle')
$template = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw
$escape = { param($s) [System.Security.SecurityElement]::Escape($s) }
$packages = @()

foreach ($arch in $Architectures) {
    Write-Host "== $arch"
    $stage = Join-Path $work $arch
    & dotnet publish (Join-Path $windows 'src\HotkeyAI\HotkeyAI.csproj') -c Release -r "win-$arch" --self-contained true `
        -p:DebugType=None -p:DebugSymbols=false -o $stage
    if ($LASTEXITCODE) { throw "dotnet publish failed for $arch" }

    Copy-Item -Recurse -LiteralPath (Join-Path $PSScriptRoot 'Assets') -Destination (Join-Path $stage 'Assets')
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $stage

    $manifest = $template.Replace('{{IDENTITY_NAME}}', (& $escape $IdentityName))
    $manifest = $manifest.Replace('{{PUBLISHER}}', (& $escape $Publisher))
    $manifest = $manifest.Replace('{{PUBLISHER_DISPLAY_NAME}}', (& $escape $PublisherDisplayName))
    $manifest = $manifest.Replace('{{VERSION}}', $Version).Replace('{{ARCH}}', $arch)
    [IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'), $manifest, [Text.UTF8Encoding]::new($false))

    # Index the scale and target-size logo variants so Windows picks the sharp one. Only the
    # logos and the manifest are indexed, not the hundreds of runtime files next to them.
    $priRoot = New-Item -ItemType Directory -Force -Path (Join-Path $work "pri-$arch")
    Copy-Item -Recurse -LiteralPath (Join-Path $PSScriptRoot 'Assets') -Destination (Join-Path $priRoot 'Assets')
    Copy-Item -LiteralPath (Join-Path $stage 'AppxManifest.xml') -Destination $priRoot
    $priConfig = Join-Path $work "priconfig-$arch.xml"
    & $makePri createconfig /cf $priConfig /dq en-US /o | Out-Host
    if ($LASTEXITCODE) { throw 'makepri createconfig failed' }
    [xml]$config = Get-Content -LiteralPath $priConfig -Raw
    # Keep every scale in the one package instead of splitting resource packages.
    [void]$config.DocumentElement.RemoveChild($config.resources.packaging)
    $config.Save($priConfig)
    & $makePri new /pr $priRoot /cf $priConfig /of (Join-Path $stage 'resources.pri') /o | Out-Host
    if ($LASTEXITCODE) { throw 'makepri new failed' }

    $package = Join-Path $OutputDirectory ("HotkeyAI_{0}_{1}.msix" -f $Version, $arch)
    & $makeAppx pack /d $stage /p $package /o | Out-Host
    if ($LASTEXITCODE) { throw "makeappx pack failed for $arch" }
    Copy-Item -LiteralPath $package -Destination $bundleInput
    $packages += $package
}

$bundle = Join-Path $OutputDirectory ("HotkeyAI_{0}.msixbundle" -f $Version)
& $makeAppx bundle /d $bundleInput /p $bundle /bv $Version /o | Out-Host
if ($LASTEXITCODE) { throw 'makeappx bundle failed' }

$files = @($packages + $bundle)
$metadata = [ordered]@{
    identityName = $IdentityName
    publisher = $Publisher
    publisherDisplayName = $PublisherDisplayName
    version = $Version
    architectures = $Architectures
    signed = $false
    purpose = if ($IdentityName -eq 'HotkeyAI.CI') { 'CI test build' } else { 'Microsoft Store submission' }
    commit = (& git -C $root rev-parse HEAD 2>$null)
    files = @($files | ForEach-Object {
        [ordered]@{ name = (Split-Path $_ -Leaf); sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $_).Hash; bytes = (Get-Item $_).Length }
    })
}
$metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'build.json') -Encoding utf8
Write-Host "Built:"
$files | ForEach-Object { Write-Host "  $_" }
