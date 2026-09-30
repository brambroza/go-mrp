#!/bin/bash
# Creates .env.demo for the Neon demo database without anyone typing or pasting a password into a chat.
#
# 1. In the Neon dashboard press "Copy" on the connection string (pooled or direct, both work).
# 2. Run:  bash scripts/setup-demo-env.sh
#
# The script reads the connection string from the clipboard, switches to the direct host, generates a
# password for the application role and a JWT signing key, creates (or re-keys) the role mrp_app in
# the database, and writes .env.demo. It prints no secrets.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="$ROOT/.env.demo"

fail() { echo "ERROR: $1" >&2; exit 1; }

command -v pbpaste >/dev/null || fail "ไม่พบ pbpaste (ต้องรันบน macOS)"
command -v psql >/dev/null || fail "ไม่พบ psql"
command -v openssl >/dev/null || fail "ไม่พบ openssl"
command -v python3 >/dev/null || fail "ไม่พบ python3"

URL="$(pbpaste | tr -d '[:space:]')"
case "$URL" in
  postgresql://*|postgres://*) ;;
  *) fail "clipboard ไม่ใช่ connection string ของ Neon — กด Copy ที่ connection string ใน Neon ก่อน แล้วรันใหม่" ;;
esac

# Parse the URL in Python so special characters in the password are decoded correctly.
PARSED="$(NEON_URL="$URL" python3 - <<'PY'
import os, sys
from urllib.parse import urlparse, unquote
u = urlparse(os.environ["NEON_URL"])
if not (u.hostname and u.username and u.password):
    sys.exit(1)
host = u.hostname.replace("-pooler", "")
db = (u.path or "/").lstrip("/") or "neondb"
print(host); print(db); print(unquote(u.username)); print(unquote(u.password))
PY
)" || fail "อ่าน connection string ไม่ได้ (ต้องมีผู้ใช้ รหัสผ่าน และ host)"

HOST="$(printf '%s\n' "$PARSED" | sed -n 1p)"
DB="$(printf '%s\n' "$PARSED" | sed -n 2p)"
OWNER="$(printf '%s\n' "$PARSED" | sed -n 3p)"
OWNER_PW="$(printf '%s\n' "$PARSED" | sed -n 4p)"

case "$OWNER_PW" in
  *";"*|*"'"*) fail "รหัสผ่านของ $OWNER มีอักขระ ; หรือ ' ซึ่งใช้ใน connection string ไม่ได้ — reset รหัสใน Neon แล้วลองใหม่" ;;
esac

# Letters and digits only, so the values are safe inside a connection string.
random_text() { openssl rand -base64 96 | tr -dc 'A-Za-z0-9' | cut -c1-"$1"; }
APP_PW="$(random_text 32)"
JWT_KEY="$(random_text 64)"
[ "${#APP_PW}" -eq 32 ] && [ "${#JWT_KEY}" -eq 64 ] || fail "สร้างค่าสุ่มไม่สำเร็จ"

echo "== 1/3 ต่อฐานข้อมูล $DB ที่ $HOST ด้วยผู้ใช้ $OWNER =="
export PGPASSWORD="$OWNER_PW" PGSSLMODE=require PGCONNECT_TIMEOUT=30
psql -h "$HOST" -U "$OWNER" -d "$DB" -Atqc "SELECT 'PostgreSQL ' || current_setting('server_version')" \
  || fail "ต่อฐานข้อมูลไม่ได้ — ตรวจว่า connection string ใน clipboard เป็นตัวล่าสุด (หลัง reset รหัสผ่าน)"

echo "== 2/3 สร้างหรือเปลี่ยนรหัสของ role mrp_app =="
# The password reaches psql through the environment, never through the command line or the output.
APP_PW="$APP_PW" psql -h "$HOST" -U "$OWNER" -d "$DB" -v ON_ERROR_STOP=1 -Atq <<'SQL'
\set pw `printenv APP_PW`
SELECT format('CREATE ROLE mrp_app LOGIN PASSWORD %L NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE', :'pw')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'mrp_app') \gexec
SELECT format('ALTER ROLE mrp_app LOGIN PASSWORD %L', :'pw') \gexec
SELECT format('GRANT CONNECT ON DATABASE %I TO mrp_app', current_database()) \gexec
SQL

UNSAFE="$(psql -h "$HOST" -U "$OWNER" -d "$DB" -Atqc \
  "SELECT (rolsuper OR rolbypassrls)::text FROM pg_roles WHERE rolname = 'mrp_app'")"
[ "$UNSAFE" = "false" ] || fail "role mrp_app ข้าม Row-Level Security ได้ (ผล: $UNSAFE) — ห้ามใช้ฐานข้อมูลนี้จนกว่าจะแก้"
MEMBER="$(psql -h "$HOST" -U "$OWNER" -d "$DB" -Atqc \
  "SELECT count(*) FROM pg_auth_members m JOIN pg_roles r ON r.oid = m.roleid JOIN pg_roles u ON u.oid = m.member WHERE u.rolname = 'mrp_app' AND (r.rolsuper OR r.rolbypassrls)")"
[ "$MEMBER" = "0" ] || fail "role mrp_app เป็นสมาชิกของ role ที่ข้าม Row-Level Security ได้ — ห้ามใช้จนกว่าจะแก้"
unset PGPASSWORD

echo "== 3/3 เขียน .env.demo =="
if [ -f "$ENV_FILE" ]; then
  cp "$ENV_FILE" "$ENV_FILE.bak"
  chmod 600 "$ENV_FILE.bak"
fi
umask 077
cat > "$ENV_FILE" <<EOF
# Demo database on Neon. Never commit this file (.env.* is git-ignored).
# Written by scripts/setup-demo-env.sh. Direct host only: tenant isolation needs a per-connection setting.

# Owner role: used only to run migrations.
ConnectionStrings__Migrator=Host=$HOST;Database=$DB;Username=$OWNER;Password=$OWNER_PW;SSL Mode=Require

# Application role (NOSUPERUSER NOBYPASSRLS). Used by the API.
ConnectionStrings__App=Host=$HOST;Database=$DB;Username=mrp_app;Password=$APP_PW;SSL Mode=Require

Jwt__SigningKey=$JWT_KEY
EOF
chmod 600 "$ENV_FILE"

# The clipboard still holds the owner password; clear it.
printf '' | pbcopy

echo "เสร็จ: เขียน $ENV_FILE แล้ว (role mrp_app ไม่ข้าม RLS) และล้าง clipboard แล้ว"
echo "ขั้นถัดไป: bash scripts/migrate-demo.sh"
