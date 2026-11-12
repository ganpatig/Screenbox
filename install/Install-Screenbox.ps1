<#
.SYNOPSIS
    Installs Screenbox, VLC edition, from the files next to this script.

.DESCRIPTION
    1. Trusts the certificate the app is signed with, by adding Screenbox.cer to
       Local Machine > Trusted People. This is why the script asks for administrator rights.
    2. Installs the app together with the two Microsoft frameworks it needs
       (Microsoft.VCLibs and Microsoft.UI.Xaml). Windows skips any that are already installed.

    Running it again installs a newer version over an older one and keeps your settings and library.
    A running Screenbox is closed first.
#>

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

# Adding a certificate to the machine store needs administrator rights, so restart elevated if needed.
$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$($MyInvocation.MyCommand.Path)`"")
    exit
}

try {
    $package = Get-ChildItem -Path $here -Filter 'Screenbox_*.msix' | Sort-Object Name -Descending | Select-Object -First 1
    $certificate = Join-Path $here 'Screenbox.cer'
    if (-not $package) { throw "Screenbox_*.msix was not found next to this script. Extract the whole zip file first." }
    if (-not (Test-Path $certificate)) { throw "Screenbox.cer was not found next to this script. Extract the whole zip file first." }

    Write-Host "Trusting the Screenbox signing certificate..." -ForegroundColor Cyan
    Import-Certificate -FilePath $certificate -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null

    $dependencies = @(Get-ChildItem -Path (Join-Path $here 'Dependencies') -Filter '*.appx' -ErrorAction SilentlyContinue | ForEach-Object FullName)

    Write-Host "Installing $($package.Name)..." -ForegroundColor Cyan
    if ($dependencies.Count -gt 0) {
        Add-AppxPackage -Path $package.FullName -DependencyPath $dependencies -ForceApplicationShutdown
    } else {
        Add-AppxPackage -Path $package.FullName -ForceApplicationShutdown
    }

    $installed = Get-AppxPackage -Name 'Screenbox' | Sort-Object Version -Descending | Select-Object -First 1
    Write-Host ""
    Write-Host "Screenbox $($installed.Version) is installed. Open it from the Start menu." -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "Installation failed: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Read-Host "Press Enter to close this window"
