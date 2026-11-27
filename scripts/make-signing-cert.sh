#!/bin/bash
# Creates a self-signed code-signing certificate "HotkeyAI Local Signing" in your login
# keychain. Local builds signed with it keep the same identity, so macOS keeps the
# Accessibility permission across rebuilds. It has no team ID, though, so saved API keys and
# the ChatGPT sign-in are lost on every rebuild; build-app.sh prefers an "Apple Development"
# certificate when you have one (Xcode → Settings → Accounts → Manage Certificates).
# It is for development on this Mac only; App Store builds use your Apple certificate.
set -euo pipefail

NAME="HotkeyAI Local Signing"
KEYCHAIN="$HOME/Library/Keychains/login.keychain-db"

if security find-certificate -c "$NAME" "$KEYCHAIN" >/dev/null 2>&1; then
  echo "Already exists: $NAME"
  exit 0
fi

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

cat > "$TMP/cert.cnf" <<EOF
[req]
distinguished_name = dn
prompt = no
x509_extensions = ext
[dn]
CN = $NAME
[ext]
basicConstraints = critical, CA:false
keyUsage = critical, digitalSignature
extendedKeyUsage = critical, codeSigning
subjectKeyIdentifier = hash
EOF

openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -config "$TMP/cert.cnf" \
  -keyout "$TMP/key.pem" -out "$TMP/cert.pem" 2>/dev/null

# Throwaway passphrase only protects the temporary .p12 while it is imported.
PASS="$(openssl rand -hex 16)"
openssl pkcs12 -export -inkey "$TMP/key.pem" -in "$TMP/cert.pem" -name "$NAME" \
  -out "$TMP/id.p12" -passout "pass:$PASS"

# -T lets codesign use the private key without asking every time.
security import "$TMP/id.p12" -k "$KEYCHAIN" -P "$PASS" -T /usr/bin/codesign >/dev/null

echo "Created: $NAME"
security find-identity -p codesigning "$KEYCHAIN" | grep "$NAME" || true
