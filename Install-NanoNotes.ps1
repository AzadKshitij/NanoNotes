#Requires -Version 5.1
<#
.SYNOPSIS
    Installs NanoNotes from a GitHub release (signed MSIX side-load).

.DESCRIPTION
    NanoNotes is signed with a self-signed certificate. Windows only trusts a
    self-signed MSIX once that certificate is in the machine's Trusted People
    store, so this script:

      1. asks for administrator rights (the certificate store is machine-wide),
      2. imports NanoNotes.cer into LocalMachine\TrustedPeople,
      3. installs the MSIX bundle (or the matching architecture's MSIX),
      4. prints how to open NanoNotes in Command Palette.

    Put this script, NanoNotes.cer and the .msixbundle in the same folder, then:

        powershell -ExecutionPolicy Bypass -File .\Install-NanoNotes.ps1

.PARAMETER PackagePath
    The .msixbundle or .msix to install. Defaults to the bundle next to this
    script, or the MSIX that matches the current CPU architecture.

.PARAMETER CertificatePath
    The public certificate to trust. Defaults to NanoNotes.cer next to this
    script.

.PARAMETER KeepExisting
    Do not remove an already installed NanoNotes before installing. Use this
    when upgrading a package that Windows will accept as an upgrade.

.PARAMETER Uninstall
    Remove NanoNotes and exit. The signing certificate is left trusted.

.PARAMETER DryRun
    Print what would happen without changing anything.
#>
[CmdletBinding()]
param(
    [string]$PackagePath,
    [string]$CertificatePath,
    [switch]$KeepExisting,
    [switch]$Uninstall,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $PSCommandPath
$packageName = 'AzadKshitij.NanoNotes'

function Write-Step([string]$Message) {
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal $identity
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-Architecture {
    switch ($env:PROCESSOR_ARCHITECTURE) {
        'ARM64' { return 'arm64' }
        default { return 'x64' }
    }
}

function Find-Package {
    if ($PackagePath) {
        if (-not (Test-Path $PackagePath)) { throw "Package not found: $PackagePath" }
        return (Resolve-Path $PackagePath).Path
    }

    $bundle = Get-ChildItem $scriptRoot -Filter '*.msixbundle' -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($bundle) { return $bundle.FullName }

    $arch = Get-Architecture
    $msix = Get-ChildItem $scriptRoot -Filter "*.msix" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match "_$arch\.msix$" } |
        Select-Object -First 1
    if ($msix) { return $msix.FullName }

    $anyMsix = Get-ChildItem $scriptRoot -Filter '*.msix' -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($anyMsix) { return $anyMsix.FullName }

    throw "No .msixbundle or .msix found next to $PSCommandPath. Download one from the release page."
}

function Invoke-Elevated {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f $PSCommandPath))
    if ($PackagePath) { $arguments += @('-PackagePath', ('"{0}"' -f $PackagePath)) }
    if ($CertificatePath) { $arguments += @('-CertificatePath', ('"{0}"' -f $CertificatePath)) }
    if ($KeepExisting) { $arguments += '-KeepExisting' }
    if ($Uninstall) { $arguments += '-Uninstall' }
    if ($DryRun) { $arguments += '-DryRun' }

    Write-Step 'Administrator rights are needed to trust the signing certificate. Approve the prompt.'

    try {
        $elevated = Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments -PassThru -Wait
    }
    catch {
        Write-Host 'Elevation was cancelled, so nothing was changed.' -ForegroundColor Yellow
        exit 1
    }

    # Report the result in this window too: the elevated window closes on its own.
    $now = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue
    if ($Uninstall) {
        if ($now) { Write-Host 'NanoNotes is still installed; check the elevated window.' -ForegroundColor Red; exit 1 }
        Write-Host 'NanoNotes is uninstalled.' -ForegroundColor Green
        exit 0
    }

    if ($now) {
        Write-Host "NanoNotes $($now.Version) is installed." -ForegroundColor Green
        Write-Host 'Press Alt+Space in Command Palette and type "notes".' -ForegroundColor Cyan
        exit 0
    }

    Write-Host "The install did not finish (exit code $($elevated.ExitCode)). Read the elevated window for the error." -ForegroundColor Red
    exit 1
}

if (-not $DryRun -and -not $Uninstall -and -not (Test-Administrator)) {
    Invoke-Elevated
}

$installed = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue

if ($Uninstall) {
    if (-not $installed) {
        Write-Host 'NanoNotes is not installed.' -ForegroundColor Yellow
        exit 0
    }

    Write-Step "Removing $packageName $($installed.Version)"
    if ($DryRun) { Write-Host 'Dry run: nothing changed.'; exit 0 }
    $installed | Remove-AppxPackage
    Write-Host 'NanoNotes removed. Your notes in Documents\Notes were left untouched.' -ForegroundColor Green
    exit 0
}

$package = Find-Package

if (-not $CertificatePath) {
    $CertificatePath = Join-Path $scriptRoot 'NanoNotes.cer'
}

if (-not (Test-Path $CertificatePath)) {
    throw "Certificate not found: $CertificatePath. Download NanoNotes.cer from the same release."
}

$certificate = Get-Item $CertificatePath
$leaf = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($certificate.FullName)

Write-Host ''
Write-Host "Package:    $(Split-Path -Leaf $package)"
Write-Host "Certificate: $($leaf.Subject)"
Write-Host "Installed:  $(if ($installed) { "$($installed.Version) ($($installed.Architecture))" } else { 'not installed' })"
Write-Host ''

if ($DryRun) {
    Write-Step "Would trust $($leaf.Thumbprint) in LocalMachine\TrustedPeople"
    if ($installed -and -not $KeepExisting) {
        Write-Step "Would remove the existing $packageName $($installed.Version)"
    }
    Write-Step "Would install $package"
    Write-Host 'Dry run: nothing changed.' -ForegroundColor Yellow
    exit 0
}

Write-Step "Trusting the NanoNotes signing certificate ($($leaf.Thumbprint))"
$alreadyTrusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
    Where-Object { $_.Thumbprint -eq $leaf.Thumbprint }

if ($alreadyTrusted) {
    Write-Host '   Certificate is already trusted.'
}
else {
    Import-Certificate -FilePath $certificate.FullName -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
}

if ($installed -and -not $KeepExisting) {
    Write-Step "Removing the existing $packageName $($installed.Version) so this build can take its place"
    $installed | Remove-AppxPackage
}

Write-Step 'Installing NanoNotes'
Add-AppxPackage -Path $package

$now = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue
if (-not $now) { throw 'The package was not installed. Check the AppXDeployment event log for details.' }

Write-Host ''
Write-Host "NanoNotes $($now.Version) is installed." -ForegroundColor Green
Write-Host ''
Write-Host 'Next steps:' -ForegroundColor Cyan
Write-Host '  1. Install PowerToys and turn on Command Palette, if you have not already.'
Write-Host '  2. Restart Command Palette (or sign out and back in).'
Write-Host '  3. Press Alt+Space and type "notes".'
Write-Host ''
Write-Host 'Your notes and clipboard history stay in Documents\Notes.' -ForegroundColor DarkGray
