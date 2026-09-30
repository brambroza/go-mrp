# Infrastructure — MRP SaaS

เอกสารนี้สำหรับทีม dev และผู้ดูแลระบบ รายละเอียด Terraform อยู่ใน [`terraform/README.md`](terraform/README.md)

```
infra/
  docker/
    postgres/init/01-roles.sh        สร้าง role สำหรับ dev (docker compose)
    web.Dockerfile                   image ของ web (build context = root ของ repo)
    web.Dockerfile.dockerignore      ignore file ของ web image
  terraform/                         GCP: Cloud Run, Cloud SQL, Secret Manager, IAM, WIF
api/Dockerfile                       image ของ API (ใช้ทั้งโหมด API และ migrate)
.github/workflows/ci.yml             build + test
.github/workflows/deploy.yml         build image → migrate → deploy → smoke test → rollback
```

## Environment

| | dev | staging | production |
|---|---|---|---|
| ที่รัน | เครื่อง dev (`docker compose`) | GCP `asia-southeast1` | GCP `asia-southeast1` |
| Database | container `postgres:17-alpine` | Cloud SQL PostgreSQL 17 (private IP) | Cloud SQL PostgreSQL 17 (private IP) |
| API / web | `dotnet run` / `pnpm web:dev` หรือ compose profile `app` | Cloud Run `mrp-api`, `mrp-web` | Cloud Run `mrp-api`, `mrp-web` |
| Migration | compose service `migrate` | Cloud Run job `mrp-migrate` | Cloud Run job `mrp-migrate` |
| Deploy | — | อัตโนมัติเมื่อ CI ผ่านบน `main` | กด Run workflow + reviewer อนุมัติ |
| `ASPNETCORE_ENVIRONMENT` | `Development` | `Staging` | `Production` |
| Scale | — | 0–2 instance | 0–3 instance |
| Deletion protection | — | ปิด | เปิด (บังคับ) |

Traffic: Browser / Mobile → Cloudflare (DNS, CDN, WAF) → Cloud Run → Cloud SQL (private IP ผ่าน Direct VPC egress)

Role ของ database เหมือนกันทุก environment: `mrp_owner` (เจ้าของ schema ใช้ตอน migrate เท่านั้น) และ
`mrp_app` (ใช้โดย API อยู่ใต้ Row-Level Security ไม่มี superuser / BYPASSRLS)

## Secret อยู่ที่ไหน

| ข้อมูล | dev | staging / production | ใครอ่านได้ |
|---|---|---|---|
| Connection string ของ API (`ConnectionStrings__App`) | `.env` → compose | Secret Manager `mrp-connection-app` | SA `mrp-api` |
| Connection string ของ migrator (`ConnectionStrings__Migrator`) | `.env` → compose | Secret Manager `mrp-connection-migrator` | SA `mrp-migrate` |
| JWT signing key (`Jwt__SigningKey`) | `.env` | Secret Manager `mrp-jwt-signing-key` | SA `mrp-api`, `mrp-migrate` |
| รหัสผ่าน `mrp_owner` | `.env` | Secret Manager `mrp-db-owner-password` | คนที่ได้รับสิทธิ์เท่านั้น |
| รหัสผ่าน `mrp_app` | `.env` | Secret Manager `mrp-db-app-password` | คนที่ได้รับสิทธิ์เท่านั้น |
| รหัสผ่าน superuser ของ Postgres | `.env` | ไม่มี (user `postgres` ของ Cloud SQL ไม่ได้ตั้งรหัสผ่าน) | — |
| Credential ของ GitHub Actions | — | ไม่มี key — ใช้ Workload Identity Federation | job ใน GitHub environment ที่ตรงกัน |
| Terraform state (มี secret ทุกตัวอยู่ข้างใน) | — | GCS bucket (private, versioning) | คนที่รัน Terraform |
| Project ID, region, ชื่อ SA | — | GitHub environment **variables** (ไม่ใช่ secret) | — |

กติกา:

- ห้าม commit `.env`, `*.tfvars`, `*.backend.hcl` (git-ignore แล้ว) — commit ได้เฉพาะไฟล์ `.example`
- ห้ามวาง secret ใน Line, issue, PR หรือ log
- ค่า secret สร้างโดย Terraform (`random_password`) ไม่มีใครต้องตั้งเอง
- เว็บ (`mrp-web`) ไม่มี secret — `NEXT_PUBLIC_API_BASE_URL` เป็นค่าสาธารณะที่ฝังใน bundle ตอน build

## Dev (docker compose)

```sh
cp .env.example .env            # ตั้งรหัสผ่านเอง
docker compose up -d postgres   # database อย่างเดียว
docker compose --profile app up -d --build   # database + migrate + api (port 5080)
curl -sf http://localhost:5080/healthz
```

หยุดเฉพาะ API: `docker compose --profile app stop api migrate`
**อย่าใช้ `docker compose down -v`** เพราะจะลบข้อมูลใน volume

Build image ด้วยมือ:

```sh
docker build -t mrp-api:dev api/
docker build -f infra/docker/web.Dockerfile \
  --build-arg NEXT_PUBLIC_API_BASE_URL=http://localhost:5080 -t mrp-web:dev .
```

## Runbook

ตัวแปรที่ใช้ในคำสั่งด้านล่าง:

```sh
export PROJECT_ID=<project-id>
export REGION=asia-southeast1
```

### Deploy

**Staging** — merge เข้า `main` → workflow CI ผ่าน → workflow Deploy รันอัตโนมัติ

**Production**

1. ตรวจว่า commit เดียวกันทำงานบน staging เรียบร้อย และผ่าน UAT
2. ใช้ skill `/deploy-check` และแจ้งลูกค้าล่วงหน้าถ้ามี downtime
3. Actions > Deploy > Run workflow > branch `main` > environment `production`
4. Reviewer อนุมัติใน GitHub (ตั้ง Required reviewers ที่ Settings > Environments > production)
5. ดูผลใน summary ของ run — มี URL และ commit ที่ deploy

ขั้นตอนใน workflow: build image (tag = commit SHA) → รัน `mrp-migrate` → deploy revision ใหม่แบบยังไม่รับ traffic →
smoke test ที่ URL `candidate` → ย้าย traffic 100% → smoke test URL จริง → ถ้าขั้นใดล้มเหลว ย้าย traffic กลับ revision เดิม

### Migrate

Migration รันอัตโนมัติใน workflow Deploy ก่อน deploy API ทุกครั้ง รันมือเมื่อจำเป็น:

```sh
gcloud run jobs execute mrp-migrate --project "$PROJECT_ID" --region "$REGION" --wait

# ดูผล
gcloud run jobs executions list --job mrp-migrate --project "$PROJECT_ID" --region "$REGION" --limit 5
gcloud logging read 'resource.type="cloud_run_job" AND resource.labels.job_name="mrp-migrate"' \
  --project "$PROJECT_ID" --limit 100 --freshness 1h --format 'value(textPayload)'
```

กติกา migration:

- **ไม่มี rollback อัตโนมัติ** — migration ต้องเข้ากันได้กับ application version ก่อนหน้า (expand → deploy → contract)
  เช่น ลบ column ต้องทำใน release ถัดจาก release ที่เลิกใช้ column นั้น
- Migration ที่ลบหรือแปลงข้อมูล: สร้าง on-demand backup ก่อน (คำสั่งอยู่ในหัวข้อ restore) และแจ้งทีมก่อนรันบน production
- Job ตั้ง `max_retries = 0` ถ้าล้มเหลวต้องมีคนดู log ก่อนรันซ้ำ

### Rollback application

Workflow Deploy rollback เองเมื่อ smoke test ไม่ผ่าน ถ้าพบปัญหาภายหลัง:

```sh
# 1. ดู revision ที่มี
gcloud run revisions list --service mrp-api --project "$PROJECT_ID" --region "$REGION" --limit 5

# 2. ย้าย traffic กลับ revision ที่ใช้ได้
gcloud run services update-traffic mrp-api --project "$PROJECT_ID" --region "$REGION" \
  --to-revisions <revision-name>=100

# 3. web ทำแบบเดียวกัน
gcloud run services update-traffic mrp-web --project "$PROJECT_ID" --region "$REGION" \
  --to-revisions <revision-name>=100

# 4. ตรวจ
curl -sf "$(gcloud run services describe mrp-api --project "$PROJECT_ID" --region "$REGION" --format 'value(status.url)')/healthz"
```

Traffic จะค้างอยู่ที่ revision นั้นจนกว่าจะรัน workflow Deploy ครั้งถัดไป (workflow ย้ายกลับไป latest เองเมื่อผ่าน smoke test)
`terraform apply` ไม่ยุ่งกับ image และ traffic

ถ้า release ที่ rollback มี migration ที่ application version เก่าใช้ไม่ได้ → เป็น P1 แจ้ง Technical Lead ทันที
แก้ด้วย forward fix (migration ใหม่) เป็นหลัก ใช้ restore เป็นทางสุดท้าย

### Restore จาก backup

Cloud SQL มี automated backup รายวัน (02:00 เวลาไทย) และ point-in-time recovery ย้อนได้ 7 วัน

**หลักการ: restore ไปยัง instance ใหม่เสมอ ห้าม restore ทับ instance ที่ใช้งานอยู่** เพื่อให้ตรวจข้อมูลก่อนสลับ และยังมีของเดิมไว้เทียบ

```sh
export INSTANCE="$(cd infra/terraform && terraform output -raw sql_instance_name)"

# ก่อนงานเสี่ยง: สร้าง on-demand backup
gcloud sql backups create --instance "$INSTANCE" --project "$PROJECT_ID" --description "before <งาน>"

# 1. clone ไปยัง instance ใหม่ ณ เวลาก่อนเกิดเหตุ (เวลาเป็น UTC)
gcloud sql instances clone "$INSTANCE" "$INSTANCE-restore" --project "$PROJECT_ID" \
  --point-in-time '2026-01-31T03:15:00Z'

# 2. ตรวจข้อมูลใน instance ใหม่ (ผ่าน Cloud SQL Studio หรือ Auth Proxy)
```

จากนั้นเลือกทางใดทางหนึ่ง:

- **เสียหายบางส่วน (เช่น tenant เดียว ลบข้อมูลผิด)** — ดึงเฉพาะข้อมูลที่ต้องการจาก instance ใหม่ด้วย `pg_dump --table` / `COPY`
  แล้วนำเข้า instance หลัก จากนั้นลบ instance ชั่วคราว ระวังเรื่อง `tenant_id` และ ledger ที่เป็น append-only
- **เสียหายทั้ง instance** — ให้ API ชี้ไป instance ใหม่:
  1. หยุดรับ traffic (ตั้ง maintenance ที่ Cloudflare)
  2. เพิ่ม secret version ใหม่ของ `mrp-connection-app` และ `mrp-connection-migrator` โดยเปลี่ยน `Host` เป็น private IP ของ instance ใหม่
  3. deploy revision ใหม่เพื่อให้อ่าน secret (รัน workflow Deploy)
  4. นำ instance ใหม่เข้า Terraform state (`terraform state rm` ตัวเก่า + `terraform import` ตัวใหม่) **ก่อน** `terraform apply` ครั้งถัดไป
     ไม่เช่นนั้น Terraform จะเขียน secret กลับไปชี้ instance เก่า

Instance ที่ clone มามี role และรหัสผ่านเดิมครบ ไม่ต้องรัน `01-roles.sql` ใหม่

เป้าหมาย: กู้ระบบได้ภายใน 1 ชั่วโมง — **ขั้นตอนนี้ยังไม่เคยซ้อมจริง** ต้องซ้อมบน staging ก่อน go-live และซ้อมซ้ำทุกเดือน
(จับเวลา และบันทึกผลในรายงานประจำเดือน)

### ตรวจอาการเบื้องต้น

```sh
# สถานะ service
gcloud run services describe mrp-api --project "$PROJECT_ID" --region "$REGION" \
  --format 'value(status.conditions[0].status,status.latestReadyRevisionName)'

# log ล่าสุดที่เป็น error
gcloud logging read 'resource.type="cloud_run_revision" AND resource.labels.service_name="mrp-api" AND severity>=ERROR' \
  --project "$PROJECT_ID" --limit 50 --freshness 1h
```

| อาการ | สาเหตุที่พบบ่อย | วิธีแก้ |
|---|---|---|
| Revision ใหม่ไม่ขึ้น, log มี `The API database role can bypass Row-Level Security` | `mrp_app` ไม่ใช่ plain role | รัน `terraform/scripts/bootstrap-db-roles.sh` |
| `password authentication failed for user "mrp_app"` | หมุนรหัสผ่านแล้วยังไม่ได้รัน bootstrap script | รัน bootstrap script แล้ว deploy ใหม่ |
| `Connection string 'App' is not configured` | SA อ่าน secret ไม่ได้ หรือ secret ไม่มี version | ตรวจ IAM ของ secret และ `terraform apply` |
| Timeout ตอนต่อ database | VPC egress / private services access | ตรวจ `vpc_access` ของ service และสถานะ Cloud SQL |
| `too many connections` | instance × pool size เกิน `max_connections` | ลด `api_max_instances` หรือเพิ่ม tier |
| Request แรกช้า | cold start (scale to zero) | ตั้ง `api_min_instances = 1` (มีค่าใช้จ่ายเพิ่ม) |

## Monitoring (ยังไม่ได้ทำ)

ตามมาตรฐานบริษัทต้องมีก่อน go-live:

- Uptime check ที่ `/healthz` ของ API และหน้าแรกของ web (UptimeRobot) แจ้งเตือนเมื่อ down เกิน 1 นาที
- Error tracking (Sentry) ทั้ง api, web, mobile
- Alert: Cloud SQL CPU > 80%, disk > 80%, memory > 85%, Cloud Run 5xx rate, response time > 3 วินาที
- แจ้งเตือนเข้า LINE ของทีม
- Budget alert ใน GCP Billing

## Escalation

- P1 (ระบบล่ม, ข้อมูลข้าม tenant, ข้อมูลหาย): แจ้ง Technical Lead + Management ทันที ตอบรับภายใน 1 ชั่วโมง
- ต้องเปลี่ยน architecture หรืองบ cloud: Technical Lead / Project Lead ก่อนลงมือ
