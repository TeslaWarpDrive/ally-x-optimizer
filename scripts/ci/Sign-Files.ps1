<#
.SYNOPSIS
    CI helper: Authenticode-signs files with the Ally Tuner certificate (SHA-256, RFC 3161 timestamp).
.DESCRIPTION
    Signs with the certificate the CI "Load signing certificate" step imported into the current user's
    certificate store, selected by thumbprint (signtool /sha1). No password or key file is ever passed on
    a command line, so neither can show up in process listings or logs.
    Expects $env:SIGNTOOL (path to signtool.exe).
#>
param(
    [Parameter(Mandatory)][string[]] $Path,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{40}$')][string] $Thumbprint
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrEmpty($env:SIGNTOOL)) { throw 'SIGNTOOL is not set.' }
if (-not (Test-Path "Cert:\CurrentUser\My\$Thumbprint")) {
    throw "Certificate $Thumbprint is not in the current user's store. Run the 'Load signing certificate' step first."
}

# Several timestamp servers, because any single one is occasionally down.
$timestampServers = @(
    'http://timestamp.digicert.com',
    'http://timestamp.sectigo.com',
    'http://timestamp.globalsign.com/tsa/r6advanced1'
)

foreach ($file in $Path) {
    if (-not (Test-Path $file)) { throw "File to sign not found: $file" }
    $signed = $false
    foreach ($server in $timestampServers) {
        # /s My = current user's Personal store; /sha1 picks our certificate there.
        & $env:SIGNTOOL sign /fd SHA256 /sha1 $Thumbprint /s My /tr $server /td SHA256 /d 'Ally Tuner' $file
        if ($LASTEXITCODE -eq 0) { $signed = $true; break }
        Write-Warning "Signing $file with timestamp server $server failed; trying the next one."
    }
    if (-not $signed) { throw "Could not sign $file." }
}
