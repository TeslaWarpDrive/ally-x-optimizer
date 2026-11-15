<#
.SYNOPSIS
    Trusts (or un-trusts) the Ally Tuner self-signed code-signing certificate on this PC.

.DESCRIPTION
    Ally Tuner builds are signed with a self-signed certificate whose private key lives only in
    GitHub encrypted secrets. Adding the PUBLIC certificate (AllyTuner-dev-signing.cer) to this
    PC's Trusted Root and Trusted Publishers stores makes Windows show "Verified publisher: Ally
    Tuner Dev Signing" and lets you confirm builds were not tampered with.

    Run once, in an administrator PowerShell, from the folder that holds the .cer file:
        powershell -ExecutionPolicy Bypass -File .\Install-SigningCert.ps1
    To undo:
        powershell -ExecutionPolicy Bypass -File .\Install-SigningCert.ps1 -Remove

    The certificate can only sign code (no CA rights, no TLS), so trusting it cannot be used to
    intercept web traffic.
#>
[CmdletBinding()]
param(
    [string] $CertificatePath = (Join-Path $PSScriptRoot 'AllyTuner-dev-signing.cer'),
    [switch] $Remove
)

$ErrorActionPreference = 'Stop'

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Please run this from an administrator PowerShell (right-click PowerShell > Run as administrator).'
}

$cert = [Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificatePath)
Write-Host "Certificate : $($cert.Subject)"
Write-Host "Thumbprint  : $($cert.Thumbprint)"
Write-Host "Valid until : $($cert.NotAfter)"

foreach ($storeName in 'Root', 'TrustedPublisher') {
    $store = [Security.Cryptography.X509Certificates.X509Store]::new($storeName, 'LocalMachine')
    $store.Open('ReadWrite')
    try {
        if ($Remove) {
            $existing = $store.Certificates.Find('FindByThumbprint', $cert.Thumbprint, $false)
            foreach ($c in $existing) { $store.Remove($c) }
            Write-Host "Removed from LocalMachine\$storeName"
        }
        else {
            $store.Add($cert)
            Write-Host "Added to LocalMachine\$storeName"
        }
    }
    finally {
        $store.Close()
    }
}
