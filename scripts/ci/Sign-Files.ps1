<#
.SYNOPSIS
    CI helper: Authenticode-signs files with the Ally Tuner certificate (SHA-256, RFC 3161 timestamp).
    Expects $env:SIGNTOOL, $env:SIGNING_PFX and $env:SIGNING_PASSWORD (from GitHub secrets).
#>
param([Parameter(Mandatory)][string[]] $Path)

$ErrorActionPreference = 'Stop'
foreach ($name in 'SIGNTOOL', 'SIGNING_PFX', 'SIGNING_PASSWORD') {
    if ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($name))) { throw "$name is not set." }
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
        & $env:SIGNTOOL sign /fd SHA256 /f $env:SIGNING_PFX /p $env:SIGNING_PASSWORD /tr $server /td SHA256 /d 'Ally Tuner' $file
        if ($LASTEXITCODE -eq 0) { $signed = $true; break }
        Write-Warning "Signing $file with timestamp server $server failed; trying the next one."
    }
    if (-not $signed) { throw "Could not sign $file." }
}
