#!/bin/sh
# Creates the database and the two roles used by the platform:
#   mrp_owner - owns the schema, runs migrations
#   mrp_app   - used by the API, subject to Row-Level Security (no superuser, no BYPASSRLS)
set -eu

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" \
  -v owner_password="$MRP_OWNER_PASSWORD" \
  -v app_password="$MRP_APP_PASSWORD" <<'SQL'
CREATE ROLE mrp_owner LOGIN PASSWORD :'owner_password' NOSUPERUSER NOBYPASSRLS;
CREATE ROLE mrp_app LOGIN PASSWORD :'app_password' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
CREATE DATABASE mrp OWNER mrp_owner;
REVOKE ALL ON DATABASE mrp FROM PUBLIC;
GRANT CONNECT ON DATABASE mrp TO mrp_app;
SQL
