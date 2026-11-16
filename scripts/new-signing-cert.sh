#!/usr/bin/env bash
# Creates the self-signed code-signing certificate and stores it in GitHub encrypted secrets.
# Run on the Mac from the repo root. The private key never touches the repo or stays on disk:
# it is generated in a temp folder, uploaded with `gh secret set`, then deleted.
# Only the PUBLIC certificate is written to signing/AllyTuner-dev-signing.cer (safe to commit).
#
# If the secret is ever lost or rotated, re-run this, commit the new .cer, and re-run
# Install-SigningCert.ps1 on the Ally (use -Remove with the old .cer first).
set -euo pipefail

REPO="${REPO:-TeslaWarpDrive/ally-x-optimizer}"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

cat > "$WORK/cert.cnf" <<'CNF'
[req]
distinguished_name = dn
x509_extensions = ext
prompt = no
[dn]
CN = Ally Tuner Dev Signing
O = Ally Tuner (self-signed)
[ext]
basicConstraints = critical, CA:FALSE
keyUsage = critical, digitalSignature
extendedKeyUsage = critical, codeSigning
subjectKeyIdentifier = hash
CNF

openssl req -x509 -newkey rsa:3072 -sha256 -days 1825 -nodes \
  -keyout "$WORK/key.pem" -out "$WORK/cert.pem" -config "$WORK/cert.cnf"

PASSWORD="$(openssl rand -base64 32)"
# AES-256 + SHA-256 PKCS#12, readable by Windows 10 1709+ / Windows Server 2019+.
openssl pkcs12 -export -inkey "$WORK/key.pem" -in "$WORK/cert.pem" -out "$WORK/signing.pfx" \
  -name "Ally Tuner Dev Signing" -passout "pass:$PASSWORD" \
  -keypbe AES-256-CBC -certpbe AES-256-CBC -macalg SHA256

mkdir -p signing
openssl x509 -in "$WORK/cert.pem" -outform der -out signing/AllyTuner-dev-signing.cer

base64 < "$WORK/signing.pfx" | tr -d '\n' | gh secret set SIGNING_CERT_PFX_BASE64 --repo "$REPO"
printf '%s' "$PASSWORD" | gh secret set SIGNING_CERT_PASSWORD --repo "$REPO"

echo "Public certificate: signing/AllyTuner-dev-signing.cer"
echo "SHA-1 thumbprint: $(openssl x509 -in "$WORK/cert.pem" -noout -fingerprint -sha1 | cut -d= -f2 | tr -d ':')"
