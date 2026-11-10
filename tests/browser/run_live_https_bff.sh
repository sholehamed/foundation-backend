#!/usr/bin/env bash
# Private trusted main-only Actions job. Nothing here is a production setup.
set -euo pipefail
mkdir -p /tmp/foundation-bff-e2e
cd /tmp/foundation-bff-e2e

openssl req -x509 -nodes -newkey rsa:2048 -days 1 \
  -keyout ca.key -out ca.crt -subj '/CN=Foundation CI disposable root' \
  -addext 'basicConstraints=critical,CA:TRUE' \
  -addext 'keyUsage=critical,keyCertSign,cRLSign' >/dev/null 2>&1
openssl req -new -nodes -newkey rsa:2048 \
  -keyout server.key -out server.csr \
  -subj '/CN=e2e-http-ci.shinera.localhost' >/dev/null 2>&1
cat >san.ext <<'EOF'
subjectAltName=DNS:e2e-http-ci.shinera.localhost,DNS:identity.shinera.localhost,DNS:other.shinera.localhost
basicConstraints=CA:FALSE
keyUsage=digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
EOF
openssl x509 -req -in server.csr -CA ca.crt -CAkey ca.key \
  -CAcreateserial -days 1 -out server.crt -extfile san.ext >/dev/null 2>&1
sudo cp ca.crt /usr/local/share/ca-certificates/foundation-e2e-root.crt
sudo update-ca-certificates >/dev/null
printf '127.0.0.1 identity.shinera.localhost e2e-http-ci.shinera.localhost other.shinera.localhost\n' |
  sudo tee -a /etc/hosts >/dev/null

cd "$GITHUB_WORKSPACE"

# FR-15D: activate Tenant dynamic navigation ONLY inside the disposable
# trusted-main Actions browser runner, and ONLY in the compiled checkout.
# The checked-in Angular environment flags stay OFF in every repository.
if [[ "${GITHUB_ACTIONS:-}" != "true" ||
      "${GITHUB_REPOSITORY:-}" != "sholehamed/foundation-backend" ||
      "${GITHUB_REF:-}" != "refs/heads/main" ||
      "${ASPNETCORE_ENVIRONMENT:-}" != "Development" ||
      "${DBNAME:-}" != "Shinera_E2E_RealBffBrowser" ||
      "${SHINERA_E2E_DB_MUTATION:-}" != "stale-jwt-ci-only" ]]; then
  echo "::error::Dynamic navigation browser pilot requires trusted-main isolated SQL CI."
  exit 1
fi
export FR15D_E2E_DYNAMIC_NAVIGATION=true
python3 - <<'PY'
from pathlib import Path
source = Path("frontend/src/environments/environment.bff-pilot.e2e.ts")
value = source.read_text(encoding="utf-8")
flag = "tenantDynamicNavigationPilotEnabled: false,"
if value.count(flag) != 1:
    raise SystemExit("Angular Tenant navigation flag contract changed; fail closed")
source.write_text(value.replace(
    flag, "tenantDynamicNavigationPilotEnabled: true,"), encoding="utf-8")
print("FR-15D: enabling dynamic Tenant navigation ONLY for disposable Angular checkout")
PY
# The main workflow already compiles its default-OFF frontend. Recompile
# the ephemeral output after swapping its test-only config; no source commit,
# real deployment, global flag, production SQL or credential change.
npm --prefix frontend run build -- --configuration bff-pilot-e2e
test -f frontend/dist/ShineraApp/browser/index.html

dotnet run --project backend/tests/Shinera.Identity.E2E.SqlMutator \
  --no-build -c Release -- ensure-e2e-database

python3 backend/tests/browser/https_bff_reverse_proxy.py \
  --angular-root frontend/dist/ShineraApp/browser \
  --cert /tmp/foundation-bff-e2e/server.crt \
  --key /tmp/foundation-bff-e2e/server.key \
  >/tmp/foundation-https-proxy.log 2>&1 &
proxy_pid=$!
dotnet run --project backend/src/Web/Web.csproj \
  --no-build --no-launch-profile -c Release \
  --urls http://127.0.0.1:5069 \
  >/tmp/foundation-api-live-bff.log 2>&1 &
api_pid=$!

cleanup() {
  kill "$proxy_pid" "$api_pid" 2>/dev/null || true
}
trap cleanup EXIT

ready=0
for attempt in $(seq 1 150); do
  if curl --silent --fail --max-time 3 \
    --resolve identity.shinera.localhost:8443:127.0.0.1 \
    https://identity.shinera.localhost:8443/api/system/Auth/captcha/new \
    >/dev/null 2>&1; then
    ready=1
    break
  fi
  if ! kill -0 "$api_pid" 2>/dev/null || \
     ! kill -0 "$proxy_pid" 2>/dev/null; then
    echo "::error::Temporary Kestrel or TLS ingress exited before readiness"
    sed -E 's/([Pp]assword)=([^; ]+)/\1=[REDACTED]/g' \
      /tmp/foundation-api-live-bff.log | tail -n 65
    tail -n 30 /tmp/foundation-https-proxy.log
    exit 1
  fi
  sleep 2
done

if [[ "$ready" != 1 ]]; then
  echo "::error::Live HTTPS API not ready"
  exit 1
fi

dotnet run --project backend/tests/Shinera.Identity.E2E.SqlMutator \
  --no-build -c Release -- seed-tenant-iam
python3 backend/tests/http/tenant_iam_chain_e2e.py
# SQL fixture is populated only after the real HTTP suite registered the
# 17 reviewed Tenant permissions. Never silently edit any real catalog.
dotnet run --project backend/tests/Shinera.Identity.E2E.SqlMutator \
  --no-build -c Release -- seed-central-tenant-navigation
cp backend/tests/browser/tenant-bff-real.spec.ts frontend/e2e/tenant-bff-real.spec.ts
cp backend/tests/browser/tenant-first-login-real.spec.ts frontend/e2e/tenant-first-login-real.spec.ts
cp backend/tests/browser/playwright.bff-real.config.ts frontend/playwright.bff-real.config.ts
cd frontend
npx playwright install --with-deps chromium
npx playwright test --config playwright.bff-real.config.ts
