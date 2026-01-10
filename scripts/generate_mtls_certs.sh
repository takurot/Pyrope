#!/bin/bash
set -euo pipefail

# Generates a local CA + server/client certs for Pyrope Sidecar mTLS.
# Output dir (git-ignored): ./.certs/
#
# Layout:
#   .certs/
#     ca/ca.crt ca/ca.key
#     sidecar/server.crt sidecar/server.key
#     garnet/client.crt garnet/client.key
#
# Notes:
# - Server cert includes SAN for "sidecar" (docker-compose service name) and "localhost".
# - Do NOT use these certs in production.

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
OUT_DIR="${ROOT_DIR}/.certs"

mkdir -p "${OUT_DIR}/ca" "${OUT_DIR}/sidecar" "${OUT_DIR}/garnet"

CA_KEY="${OUT_DIR}/ca/ca.key"
CA_CRT="${OUT_DIR}/ca/ca.crt"

SERVER_KEY="${OUT_DIR}/sidecar/server.key"
SERVER_CSR="${OUT_DIR}/sidecar/server.csr"
SERVER_CRT="${OUT_DIR}/sidecar/server.crt"

CLIENT_KEY="${OUT_DIR}/garnet/client.key"
CLIENT_CSR="${OUT_DIR}/garnet/client.csr"
CLIENT_CRT="${OUT_DIR}/garnet/client.crt"

if [[ ! -f "${CA_KEY}" || ! -f "${CA_CRT}" ]]; then
  echo "[mtls] Generating CA..."
  openssl genrsa -out "${CA_KEY}" 2048
  openssl req -x509 -new -nodes -key "${CA_KEY}" -sha256 -days 3650 \
    -subj "/CN=Pyrope-Dev-CA" \
    -out "${CA_CRT}"
else
  echo "[mtls] CA already exists, skipping."
fi

echo "[mtls] Generating Sidecar server cert..."
openssl genrsa -out "${SERVER_KEY}" 2048
openssl req -new -key "${SERVER_KEY}" -subj "/CN=pyrope-sidecar" -out "${SERVER_CSR}"

cat > "${OUT_DIR}/sidecar/server.ext" <<'EOF'
basicConstraints=CA:FALSE
keyUsage=digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectAltName=@alt_names

[alt_names]
DNS.1=sidecar
DNS.2=localhost
IP.1=127.0.0.1
EOF

openssl x509 -req -in "${SERVER_CSR}" -CA "${CA_CRT}" -CAkey "${CA_KEY}" -CAcreateserial \
  -out "${SERVER_CRT}" -days 365 -sha256 -extfile "${OUT_DIR}/sidecar/server.ext"

echo "[mtls] Generating Garnet client cert..."
openssl genrsa -out "${CLIENT_KEY}" 2048
openssl req -new -key "${CLIENT_KEY}" -subj "/CN=pyrope-garnet" -out "${CLIENT_CSR}"

cat > "${OUT_DIR}/garnet/client.ext" <<'EOF'
basicConstraints=CA:FALSE
keyUsage=digitalSignature,keyEncipherment
extendedKeyUsage=clientAuth
EOF

openssl x509 -req -in "${CLIENT_CSR}" -CA "${CA_CRT}" -CAkey "${CA_KEY}" -CAcreateserial \
  -out "${CLIENT_CRT}" -days 365 -sha256 -extfile "${OUT_DIR}/garnet/client.ext"

rm -f "${SERVER_CSR}" "${CLIENT_CSR}" "${OUT_DIR}/sidecar/server.ext" "${OUT_DIR}/garnet/client.ext"

echo "[mtls] Done. Certs written to ${OUT_DIR}"
echo "      docker-compose.yml expects these paths mounted at /certs/*"

