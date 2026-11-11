# Builds CL-Wallpaper.exe with the C# compiler that ships with Windows (.NET Framework 4.8).
# No SDK, no NuGet, nothing to install.  Usage:  powershell -ExecutionPolicy Bypass -File build.ps1
#
# Code signing (optional), so Windows SmartScreen and the built-in updater can trust the exe:
#   -Pfx cert.pfx [-PfxPassword ...]     a code-signing certificate file, or
#   -Thumbprint ABC123...                one installed in Cert:\CurrentUser\My (e.g. on a USB token)
#   -TimestampUrl http://...             RFC 3161 timestamp server (default DigiCert; "none" = no timestamp)
#   -UpdateJson                          also writes update.json (version + SHA-256) next to the exe, for the updater
param([string]$Out = "", [string]$Pfx = "", [string]$PfxPassword = "", [string]$Thumbprint = "",
      [string]$TimestampUrl = "http://timestamp.digicert.com", [switch]$UpdateJson, [string]$DownloadUrl = "CL-Wallpaper.exe")
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw   = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path "$fw\csc.exe")) { $fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$csc  = Join-Path $fw 'csc.exe'
$wpf  = Join-Path $fw 'WPF'

# 1. icon
$ico = Join-Path $root 'assets\app.ico'
if (-not (Test-Path $ico)) {
    $tool = Join-Path $env:TEMP 'clw-makeicon.exe'
    & $csc /nologo /target:exe /out:$tool /r:System.Drawing.dll (Join-Path $root 'tools\MakeIcon.cs')
    if ($LASTEXITCODE -ne 0) { throw 'icon tool build failed' }
    & $tool $ico
    Remove-Item $tool -ErrorAction SilentlyContinue
}

# 2. application
$sources = Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.cs | ForEach-Object { $_.FullName }
$xaml    = Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.xaml | ForEach-Object { "/resource:$($_.FullName),CLWallpaper.$($_.Name)" }
$out     = if ($Out) { $Out } else { Join-Path $root 'CL-Wallpaper.exe' }

$refs = @(
  'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Xml.dll',
  "$fw\System.Xaml.dll", "$wpf\WindowsBase.dll", "$wpf\PresentationCore.dll", "$wpf\PresentationFramework.dll",
  'Microsoft.VisualBasic.dll'
) | ForEach-Object { "/r:$_" }

$manifest = Join-Path $root 'assets\app.manifest'
& $csc /nologo /noconfig /target:winexe /optimize+ /debug- /codepage:65001 /platform:anycpu /warn:3 `
    "/out:$out" "/win32icon:$ico" "/win32manifest:$manifest" `
    "/resource:$ico,CLWallpaper.app.ico" @xaml @refs @sources
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

# 3. signature
if ($Pfx -or $Thumbprint) {
    if ($Pfx) {
        $pw = if ($PfxPassword) { ConvertTo-SecureString $PfxPassword -AsPlainText -Force } else { $null }
        $cert = Get-PfxCertificate -FilePath $Pfx -Password $pw
    } else {
        $cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -CodeSigningCert | Where-Object Thumbprint -eq $Thumbprint | Select-Object -First 1
    }
    if (-not $cert) { throw "certificate not found" }
    $sig = @{ FilePath = $out; Certificate = $cert; HashAlgorithm = "SHA256" }
    if ($TimestampUrl -and $TimestampUrl -ne "none") { $sig.TimestampServer = $TimestampUrl }
    $r = Set-AuthenticodeSignature @sig
    if (-not $r.SignerCertificate) { throw "signing failed: $($r.StatusMessage)" }
    Write-Host "Signed by $($cert.Subject) ($($r.Status))"
}

# 4. update manifest
if ($UpdateJson) {
    $ver = (Get-Item $out).VersionInfo.ProductVersion
    $sha = (Get-FileHash $out -Algorithm SHA256).Hash
    $json = "{`"version`":`"$ver`",`"url`":`"$DownloadUrl`",`"sha256`":`"$sha`",`"notes`":`"`"}"
    [IO.File]::WriteAllText((Join-Path (Split-Path $out) "update.json"), $json)
    Write-Host "update.json -> $ver $sha"
}
$size = [math]::Round((Get-Item $out).Length / 1KB)
Write-Host "OK -> $out ($size Ko)"
