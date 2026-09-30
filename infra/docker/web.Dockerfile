# syntax=docker/dockerfile:1

# MRP web image (Next.js standalone output in a pnpm workspace).
# Build context is the REPOSITORY ROOT, because the web app depends on packages/api-client:
#   docker build -f infra/docker/web.Dockerfile \
#     --build-arg NEXT_PUBLIC_API_BASE_URL=https://api.example.com \
#     -t mrp-web:dev .
#
# The ignore file for this build is infra/docker/web.Dockerfile.dockerignore (BuildKit picks up
# "<dockerfile name>.dockerignore" automatically), so no root-level .dockerignore is needed.
#
# Requirements on the web app (owned by the web team, see web/next.config.ts):
#   - output: "standalone"
#   - outputFileTracingRoot pointing at the repository root
# With those two settings the standalone server ends up at .next/standalone/web/server.js.

ARG NODE_VERSION=24

# ---------- base ----------
FROM node:${NODE_VERSION}-alpine AS base
# libc6-compat: some prebuilt native modules (swc, sharp) expect glibc symbols on Alpine.
RUN apk add --no-cache libc6-compat
ENV PNPM_HOME=/pnpm \
    NEXT_TELEMETRY_DISABLED=1 \
    CI=true
ENV PATH="$PNPM_HOME:$PATH"
WORKDIR /repo
# The pnpm version comes from "packageManager" in the root package.json (single source of truth).
COPY package.json ./
RUN corepack enable && corepack install

# ---------- deps ----------
# Only manifests are copied, so this layer is reused until a package.json or the lockfile changes.
FROM base AS deps
COPY pnpm-lock.yaml pnpm-workspace.yaml .npmrc ./
COPY web/package.json web/
COPY packages/api-client/package.json packages/api-client/
# "--filter @mrp/web..." installs the web app and its workspace dependencies, not the mobile app.
RUN --mount=type=cache,id=mrp-pnpm,target=/pnpm/store \
    pnpm install --frozen-lockfile --filter "@mrp/web..."

# ---------- build ----------
FROM deps AS build
# NEXT_PUBLIC_* values are inlined into the JavaScript bundle at build time,
# so one image is built per environment.
ARG NEXT_PUBLIC_API_BASE_URL
ENV NEXT_PUBLIC_API_BASE_URL=${NEXT_PUBLIC_API_BASE_URL} \
    NODE_ENV=production
RUN test -n "$NEXT_PUBLIC_API_BASE_URL" \
    || (echo "Build argument NEXT_PUBLIC_API_BASE_URL is required" >&2; exit 1)
COPY packages/api-client/ packages/api-client/
COPY web/ web/
RUN pnpm --filter @mrp/web build
# web/public is optional in Next.js; make sure the COPY in the runtime stage never fails.
RUN mkdir -p web/public

# ---------- runtime ----------
FROM node:${NODE_VERSION}-alpine AS runtime
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    HOSTNAME=0.0.0.0
WORKDIR /app
# The standalone folder mirrors the workspace layout: node_modules/, packages/, web/server.js.
COPY --from=build --chown=node:node /repo/web/.next/standalone ./
# Static assets and public files are not part of the standalone trace.
COPY --from=build --chown=node:node /repo/web/.next/static ./web/.next/static
COPY --from=build --chown=node:node /repo/web/public ./web/public

# Non-root user that ships with the official Node image (UID 1000).
USER node
EXPOSE 3000
CMD ["node", "web/server.js"]
