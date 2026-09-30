-- Role bootstrap for Cloud SQL. Equivalent of infra/docker/postgres/init/01-roles.sh.
--
-- Run ONCE per environment after `terraform apply`, and again whenever the mrp_app password is
-- rotated. The script is idempotent.
--
-- Connect as:  mrp_owner (created by Terraform), database "postgres"
-- Run with:    psql, because it uses psql variables and \gexec. Use scripts/bootstrap-db-roles.sh,
--              which reads the password from Secret Manager and never prints it.
-- Variable:    app_password   password of mrp_app (Secret Manager: <prefix>-db-app-password)
--
-- Why this is not done by Terraform: every user created through the Cloud SQL API is a member of
-- "cloudsqlsuperuser" and has CREATEROLE and CREATEDB. The API must connect as a plain role, so
-- mrp_app is created here with SQL, where the attributes can be chosen.
--
-- Differences from the local docker script:
--   * mrp_owner and the database "mrp" already exist (Terraform created them).
--   * mrp_owner is a member of cloudsqlsuperuser. It is not a PostgreSQL superuser and has no
--     BYPASSRLS, and it is used by the migrate job only.

\set ON_ERROR_STOP on

-- 1. Create mrp_app as a plain role when it does not exist yet.
SELECT format(
  'CREATE ROLE mrp_app LOGIN PASSWORD %L NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION',
  :'app_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'mrp_app')
\gexec

-- 2. Keep password and attributes in line on every run. SUPERUSER and BYPASSRLS are not listed:
--    only a superuser may mention them in ALTER ROLE; they are verified in step 5 instead.
SELECT format(
  'ALTER ROLE mrp_app WITH LOGIN PASSWORD %L NOCREATEDB NOCREATEROLE',
  :'app_password')
\gexec

-- 3. Remove the Cloud SQL admin membership if the role was ever created through the console,
--    gcloud or google_sql_user. If this statement fails with "permission denied to revoke role",
--    mrp_owner has no ADMIN option on that membership: delete the user with
--    `gcloud sql users delete mrp_app --instance <instance>` (only possible while the role owns
--    nothing and before the first migration has granted it privileges) and run this script again.
SELECT 'REVOKE cloudsqlsuperuser FROM mrp_app'
WHERE EXISTS (
  SELECT 1
  FROM pg_auth_members m
  JOIN pg_roles granted ON granted.oid = m.roleid
  JOIN pg_roles member ON member.oid = m.member
  WHERE granted.rolname = 'cloudsqlsuperuser' AND member.rolname = 'mrp_app')
\gexec

-- 4. Database ownership and access.
ALTER DATABASE mrp OWNER TO mrp_owner;
REVOKE ALL ON DATABASE mrp FROM PUBLIC;
GRANT CONNECT ON DATABASE mrp TO mrp_app;

-- 5. Fail loudly when mrp_app is anything but a plain role. The API performs the same check
--    (rolsuper OR rolbypassrls) at startup and refuses to start outside Development.
DO $$
DECLARE
  problems text;
BEGIN
  SELECT concat_ws(', ',
           CASE WHEN r.rolsuper THEN 'SUPERUSER' END,
           CASE WHEN r.rolbypassrls THEN 'BYPASSRLS' END,
           CASE WHEN r.rolcreaterole THEN 'CREATEROLE' END,
           CASE WHEN r.rolcreatedb THEN 'CREATEDB' END,
           CASE WHEN r.rolreplication THEN 'REPLICATION' END,
           (SELECT 'member of ' || string_agg(granted.rolname, '/')
              FROM pg_auth_members m
              JOIN pg_roles granted ON granted.oid = m.roleid
             WHERE m.member = r.oid))
    INTO problems
    FROM pg_roles r
   WHERE r.rolname = 'mrp_app';

  IF problems IS NULL THEN
    RAISE EXCEPTION 'role mrp_app does not exist';
  ELSIF problems <> '' THEN
    RAISE EXCEPTION 'role mrp_app is not a plain role: %', problems;
  END IF;

  RAISE NOTICE 'mrp_app is a plain role (no superuser, no BYPASSRLS, no admin membership)';
END
$$;
