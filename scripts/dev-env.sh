#!/bin/sh
# Source this file to point the API at the local docker-compose database:
#   . scripts/dev-env.sh && dotnet run --project api/src/Mrp.Api
# Values come from .env (never committed).
ROOT="$(cd "$(dirname "${BASH_SOURCE:-$0}")/.." && pwd)"
if [ ! -f "$ROOT/.env" ]; then
  echo "Missing $ROOT/.env - copy .env.example and set values" >&2
  return 1 2>/dev/null || exit 1
fi
set -a
. "$ROOT/.env"
set +a
export ConnectionStrings__App="Host=localhost;Port=${POSTGRES_PORT:-5433};Database=mrp;Username=mrp_app;Password=${MRP_APP_PASSWORD}"
export ConnectionStrings__Migrator="Host=localhost;Port=${POSTGRES_PORT:-5433};Database=mrp;Username=mrp_owner;Password=${MRP_OWNER_PASSWORD}"
export Jwt__SigningKey="${JWT_SIGNING_KEY}"
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
