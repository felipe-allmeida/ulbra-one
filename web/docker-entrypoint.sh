#!/bin/sh
# Generates the runtime env file consumed by the SPA (web/src/config/env.ts reads
# window.__ENV__). Runs at container startup so a single built image can be pointed
# at any environment via container envs. nginx:alpine executes this from
# /docker-entrypoint.d/ before launching nginx.
set -eu

ENV_FILE="/usr/share/nginx/html/env.js"

cat >"$ENV_FILE" <<EOF
window.__ENV__ = {
  VITE_API_BASE_URL: "${VITE_API_BASE_URL:-}",
  VITE_GOOGLE_CLIENT_ID: "${VITE_GOOGLE_CLIENT_ID:-}",
  VITE_ENVIRONMENT: "${VITE_ENVIRONMENT:-}"
};
EOF

echo "[40-generate-env] wrote $ENV_FILE (VITE_API_BASE_URL=${VITE_API_BASE_URL:-<empty>})"
