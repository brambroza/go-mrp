#!/bin/bash
# Applies schema migrations and Row-Level Security to the demo database (Neon).
# Reads connection settings from .env.demo (never committed). Prints no secrets.
#
# Usage:  bash scripts/migrate-demo.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="$ROOT/.env.demo"

fail() { echo "ERROR: $1" >&2; exit 1; }

[ -f "$ENV_FILE" ] || fail "ไม่พบ $ENV_FILE — สร้างไฟล์ตามตัวอย่างใน docs/deploy-demo.md ก่อน"

# Read KEY=VALUE lines without letting the shell interpret ';' inside connection strings.
read_setting() {
  grep -E "^$1=" "$ENV_FILE" | head -1 | cut -d= -f2- || true
}

MIGRATOR="$(read_setting ConnectionStrings__Migrator)"
APP="$(read_setting ConnectionStrings__App)"
JWT_KEY="$(read_setting Jwt__SigningKey)"

# Name the settings that still hold a placeholder, without printing any value.
pending=""
case "$MIGRATOR" in *CHANGE_ME*) pending="$pending ConnectionStrings__Migrator(รหัสของ neondb_owner)" ;; esac
case "$APP" in *CHANGE_ME*) pending="$pending ConnectionStrings__App(รหัสของ mrp_app)" ;; esac
case "$JWT_KEY" in *CHANGE_ME*) pending="$pending Jwt__SigningKey" ;; esac
if [ -n "$pending" ]; then
  echo "ไฟล์: $ENV_FILE (แก้ไขล่าสุด: $(date -r "$ENV_FILE" '+%H:%M:%S'))" >&2
  fail "ยังไม่ได้แทนค่า CHANGE_ME ใน:$pending"
fi
[ -n "$MIGRATOR" ] || fail "ไม่มี ConnectionStrings__Migrator ใน .env.demo"
[ -n "$APP" ] || fail "ไม่มี ConnectionStrings__App ใน .env.demo"
[ "${#JWT_KEY}" -ge 32 ] || fail "Jwt__SigningKey ต้องยาวอย่างน้อย 32 ตัวอักษร"

# Tenant isolation relies on a session setting, which a transaction pooler does not preserve.
case "$MIGRATOR$APP" in
  *-pooler*) fail "connection string ใช้ host แบบ pooled (-pooler) — ต้องใช้ host แบบ direct" ;;
esac
case "$MIGRATOR$APP" in
  *postgresql://*|*postgres://*) fail "connection string เป็นแบบ URL — ต้องเป็นรูปแบบ Host=...;Database=...;Username=...;Password=...;SSL Mode=Require" ;;
esac
case "$APP" in
  *Username=mrp_app*) ;;
  *) fail "ConnectionStrings__App ต้องใช้ผู้ใช้ mrp_app (role ที่ไม่ข้าม RLS)" ;;
esac

export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export ASPNETCORE_ENVIRONMENT=Production
export ConnectionStrings__Migrator="$MIGRATOR"
export ConnectionStrings__App="$APP"
export Jwt__SigningKey="$JWT_KEY"
export Database__AppRole=mrp_app

command -v dotnet >/dev/null || fail "ไม่พบ dotnet — ติดตั้ง .NET SDK 10 ก่อน"

echo "== 1/3 build =="
cd "$ROOT/api"
dotnet build src/Mrp.Api -c Release --nologo -v quiet | tail -3

echo "== 2/3 migrate (สร้าง schema + เปิด Row-Level Security) =="
# Connection errors can echo the host but never the password; output is filtered to the useful lines.
dotnet run --project src/Mrp.Api -c Release --no-build -- migrate 2>&1 \
  | grep -E "Migrating schema|Applying migration|Row-Level Security|Unhandled|Exception|error|denied|timeout" \
  | grep -v "__ef_migrations_history" || true

echo "== 3/3 ตรวจผลด้วย role ของ API =="
if command -v psql >/dev/null; then
  host="$(printf '%s' "$APP" | tr ';' '\n' | grep -i '^Host=' | cut -d= -f2-)"
  db="$(printf '%s' "$APP" | tr ';' '\n' | grep -i '^Database=' | cut -d= -f2-)"
  pass="$(printf '%s' "$APP" | tr ';' '\n' | grep -i '^Password=' | cut -d= -f2-)"
  PGPASSWORD="$pass" PGSSLMODE=require psql -h "$host" -U mrp_app -d "$db" -At -F ' | ' <<'SQL'
SELECT 'role ของ API ข้าม RLS ได้ (ต้องเป็น f)', rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user;
SELECT 'จำนวนตารางที่มี tenant_id', count(*) FROM information_schema.columns WHERE column_name = 'tenant_id' AND table_schema IN ('platform','masters','inventory','purchasing','production');
SELECT 'ตารางที่มี tenant_id แต่ไม่ได้เปิด RLS (ต้องเป็น 0)', count(*)
FROM information_schema.columns c
JOIN pg_namespace n ON n.nspname = c.table_schema
JOIN pg_class k ON k.relnamespace = n.oid AND k.relname = c.table_name AND k.relkind = 'r'
WHERE c.column_name = 'tenant_id' AND (NOT k.relrowsecurity OR NOT k.relforcerowsecurity);
SELECT 'แถวที่ role ของ API เห็นโดยไม่ระบุบริษัท (ต้องเป็น 0)', count(*) FROM platform.users;
SQL
else
  echo "ไม่พบ psql — ข้ามการตรวจผล"
fi

echo "เสร็จ"
