# syntax=docker/dockerfile:1

# ---------- build ----------
FROM node:22-alpine AS build
WORKDIR /app

# Enable the pinned pnpm via corepack.
RUN corepack enable && corepack prepare pnpm@11.5.0 --activate

# Install deps first (layer cache) — copy only the manifests the install graph needs.
COPY web/package.json web/pnpm-lock.yaml web/pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile

# Build the SPA. VITE_API_BASE_URL is intentionally NOT baked in here — the runtime
# env.js (generated at container startup) supplies it, so one image fits any env.
COPY web/ ./
RUN pnpm build

# ---------- runtime ----------
FROM nginx:alpine AS runtime

# SPA-aware server config (fallback, gzip, caching, security headers).
COPY web/nginx.conf /etc/nginx/conf.d/default.conf

# Generates /usr/share/nginx/html/env.js from container envs before nginx starts.
# nginx:alpine runs every executable in /docker-entrypoint.d/ on boot.
COPY web/docker-entrypoint.sh /docker-entrypoint.d/40-generate-env.sh
RUN chmod +x /docker-entrypoint.d/40-generate-env.sh

COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80
