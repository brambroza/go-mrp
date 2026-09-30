#!/bin/sh
# Creates the plain "mrp_app" role on Cloud SQL by running sql/01-roles.sql as mrp_owner.
#
# Prerequisites:
#   - gcloud authenticated as a human who may read the two db-*-password secrets
#   - psql (PostgreSQL client 15+)
#   - a Cloud SQL Auth Proxy already listening locally (see README.md, section "bootstrap roles")
#
# Usage:
#   PROJECT_ID=<project> ./scripts/bootstrap-db-roles.sh
# Optional: NAME_PREFIX (default mrp), PGHOST (default 127.0.0.1), PGPORT (default 6543)
#
# Passwords are read from Secret Manager into environment variables of this process only;
# they are never printed and never written to disk.
set -eu

: "${PROJECT_ID:?set PROJECT_ID to the GCP project of the environment}"
NAME_PREFIX="${NAME_PREFIX:-mrp}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

read_secret() {
  gcloud secrets versions access latest --project "$PROJECT_ID" --secret "$NAME_PREFIX-$1"
}

PGPASSWORD="$(read_secret db-owner-password)"
MRP_APP_PASSWORD="$(read_secret db-app-password)"
export PGPASSWORD

# The password reaches psql through stdin (\set), not through the command line, so it does not
# show up in the process list. Terraform generates it from letters and digits only.
{
  printf '\\set app_password %s\n' "$MRP_APP_PASSWORD"
  cat "$SCRIPT_DIR/../sql/01-roles.sql"
} | psql \
  --host "${PGHOST:-127.0.0.1}" \
  --port "${PGPORT:-6543}" \
  --username mrp_owner \
  --dbname postgres \
  --no-psqlrc \
  --quiet \
  --set ON_ERROR_STOP=1

echo "Done: mrp_app exists as a plain role in project $PROJECT_ID."
