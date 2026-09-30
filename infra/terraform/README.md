# Terraform — MRP บน GCP (asia-southeast1)

Root module เดียว ใช้กับทุก environment โดยแยก **state** (ผ่าน `-backend-config`) และ **tfvars** ต่อ environment

> สถานะการตรวจสอบ: ผ่าน `terraform fmt` และ `terraform validate` แล้ว (Terraform 1.9 และ 1.13, provider google 6.50.0)
> **ยังไม่เคย `plan`/`apply` กับ GCP project จริง** — ครั้งแรกให้ apply ที่ staging ก่อนและอ่าน plan ทุกบรรทัด

## สิ่งที่สร้าง

| ไฟล์ | Resource |
|---|---|
| `main.tf` | เปิด API ที่จำเป็น, Artifact Registry (Docker) พร้อม cleanup policy |
| `network.tf` | VPC, subnet สำหรับ Cloud Run Direct VPC egress, private services access สำหรับ Cloud SQL |
| `database.tf` | Cloud SQL PostgreSQL 17 (private IP, backup + PITR), database `mrp`, user `mrp_owner` |
| `secrets.tf` | Secret Manager: connection string 2 ตัว, JWT signing key, รหัสผ่าน DB 2 ตัว |
| `iam.tf` | Service account ต่อ workload, deployer SA, Workload Identity Federation สำหรับ GitHub Actions |
| `run.tf` | Cloud Run service `mrp-api`, `mrp-web`, Cloud Run job `mrp-migrate`, worker (ปิดอยู่) |
| `domains.tf` | Custom domain mapping (optional) |
| `sql/01-roles.sql` | สร้าง role `mrp_app` แบบ plain role (รันมือ 1 ครั้งต่อ environment) |
| `scripts/bootstrap-db-roles.sh` | ตัวช่วยรัน `01-roles.sql` โดยอ่านรหัสผ่านจาก Secret Manager |

## การตัดสินใจด้าน network: Direct VPC egress

เลือก **Direct VPC egress** แทน Serverless VPC Access connector เพราะ

- connector ต้องมี VM ขั้นต่ำ 2 ตัวเปิดตลอดเวลา มีค่าใช้จ่ายคงที่ทุกเดือนแม้ไม่มี traffic ส่วน Direct VPC egress ไม่มีค่า instance เพิ่ม
- ไม่มี resource เพิ่มให้ดูแล (แค่ subnet 1 อัน) และใช้ได้ทั้ง Cloud Run service และ job ใน `asia-southeast1`
- ตั้ง `egress = PRIVATE_RANGES_ONLY` — เฉพาะ traffic ไป private IP (Cloud SQL) ที่วิ่งผ่าน VPC ส่วนการเรียก LINE / Expo Push / R2 ออก internet ตรง จึง **ไม่ต้องมี Cloud NAT**

Cloud SQL ใช้ private IP ผ่าน private services access (VPC peering) ซึ่งไม่มีค่าใช้จ่าย และบังคับ `ssl_mode = ENCRYPTED_ONLY`

ข้อจำกัดที่ต้องรู้: Direct VPC egress ใช้ IP จาก subnet ตามจำนวน instance (subnet `/24` ที่ตั้งไว้เหลือเฟือ) และ cold start อาจช้ากว่าเล็กน้อย

## สิ่งที่ต้องมีก่อนเริ่ม

- Terraform >= 1.9 (หรือใช้ Docker image `hashicorp/terraform`)
- `gcloud` ที่ login แล้ว และมีสิทธิ์ Owner (หรือเทียบเท่า) ใน project เป้าหมาย
- GCP project ที่ผูก billing แล้ว — **แนะนำ 1 project ต่อ 1 environment** (แยก IAM และค่าใช้จ่ายชัดเจน ชื่อ resource ซ้ำกันได้)
  ถ้าจำเป็นต้องใช้ project เดียว ให้ตั้ง `name_prefix` ต่างกัน เช่น `mrp-stg` และตั้ง GitHub variable `NAME_PREFIX` ให้ตรงกัน
- `psql` และ [Cloud SQL Auth Proxy](https://cloud.google.com/sql/docs/postgres/sql-proxy) สำหรับขั้นตอนสร้าง role

## Bootstrap (ทำครั้งเดียวต่อ environment)

ตัวอย่างใช้ staging — production ทำเหมือนกันโดยเปลี่ยนชื่อไฟล์

### 1. สร้าง state bucket

State มีรหัสผ่านและ JWT key อยู่ข้างใน bucket จึงต้อง private และเปิด versioning

```sh
export PROJECT_ID=<project-id>
export STATE_BUCKET=<ชื่อ bucket ที่ไม่ซ้ำใคร>

gcloud storage buckets create "gs://$STATE_BUCKET" \
  --project "$PROJECT_ID" \
  --location asia-southeast1 \
  --uniform-bucket-level-access \
  --public-access-prevention
gcloud storage buckets update "gs://$STATE_BUCKET" --versioning
```

ให้สิทธิ์เข้าถึง bucket เฉพาะคนที่รัน Terraform เท่านั้น

### 2. เตรียมไฟล์ config (ไม่ commit)

```sh
cd infra/terraform
cp envs/backend.hcl.example     envs/staging.backend.hcl   # แก้ bucket และ prefix
cp envs/staging.tfvars.example  envs/staging.tfvars        # แก้ project_id, github_repository
```

`*.tfvars` และ `*.backend.hcl` ถูก git-ignore แล้ว เหลือเฉพาะ `*.example` ที่ commit

### 3. Init / plan / apply

```sh
gcloud auth application-default login

terraform init -reconfigure -backend-config=envs/staging.backend.hcl
terraform plan  -var-file=envs/staging.tfvars -out=staging.tfplan
terraform apply staging.tfplan
```

สลับ environment ต้อง `terraform init -reconfigure` ด้วย backend config ของ environment นั้นทุกครั้ง
ก่อน apply ให้ตรวจบรรทัด `Initializing the backend` ว่าเป็น prefix ที่ถูกต้อง — **apply ผิด state คือความเสี่ยงหลักของโครงสร้างนี้**

ครั้งแรก Cloud SQL ใช้เวลาสร้างราว 10–15 นาที Cloud Run service จะถูกสร้างด้วย image ตัวอย่างของ Google
(`us-docker.pkg.dev/cloudrun/container/hello`) เพราะ registry ยังว่าง หลังจากนั้น image และ traffic เป็นหน้าที่ของ deploy workflow
(Terraform ตั้ง `ignore_changes` ไว้ จึงไม่ย้อน image กลับ)

### 4. สร้าง role `mrp_app` (สำคัญ)

**ทำไมต้องทำมือ:** user ที่สร้างผ่าน Cloud SQL API (`google_sql_user`, console, `gcloud sql users create`) จะเป็นสมาชิกของ
`cloudsqlsuperuser` และมี `CREATEROLE` + `CREATEDB` เสมอ ซึ่ง Terraform ปิดไม่ได้
แต่ API ต้องต่อด้วย role ธรรมดาที่อยู่ใต้ Row-Level Security — API ตรวจ `rolsuper OR rolbypassrls` ตอน start และ
**ไม่ยอม start** นอก Development ถ้า role ข้าม RLS ได้

การแบ่งหน้าที่:

| Role | ใครสร้าง | ใช้โดย | หมายเหตุ |
|---|---|---|---|
| `mrp_owner` | Terraform (`google_sql_user`) | job `mrp-migrate` เท่านั้น | เป็นสมาชิก `cloudsqlsuperuser` (ไม่ใช่ superuser จริง) เป็นเจ้าของ database และทุก schema |
| `mrp_app` | `sql/01-roles.sql` | service `mrp-api` | `NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE` ไม่เป็นสมาชิก role ใด |

รหัสผ่านของทั้งสอง role ถูกสร้างโดย Terraform และเก็บใน Secret Manager (`mrp-db-owner-password`, `mrp-db-app-password`)
connection string ใน `mrp-connection-app` ใช้รหัสผ่านตัวเดียวกัน script จึงต้องตั้งรหัสผ่านของ `mrp_app` ให้ตรงกับ secret

ขั้นตอน (instance มีแค่ private IP จึงต้องเปิดทางเข้าชั่วคราว):

```sh
# 4.1 เปิด public IP ชั่วคราว (ไม่มี authorized network ใดๆ เข้าได้ผ่าน Auth Proxy + IAM เท่านั้น)
terraform apply -var-file=envs/staging.tfvars -var db_public_ip_enabled=true

# 4.2 เปิด proxy ใน terminal อีกหน้าต่าง
cloud-sql-proxy --port 6543 "$(terraform output -raw sql_instance_connection_name)"

# 4.3 รัน script (อ่านรหัสผ่านจาก Secret Manager ไม่แสดงบนจอ)
PROJECT_ID=<project-id> ./scripts/bootstrap-db-roles.sh

# 4.4 ปิด public IP
terraform apply -var-file=envs/staging.tfvars
```

ผลที่ถูกต้องคือข้อความ `NOTICE: mrp_app is a plain role ...` ถ้า role มี attribute เกิน script จะ error และหยุด

Script เป็น idempotent รันซ้ำได้ และต้องรันซ้ำทุกครั้งที่หมุนรหัสผ่าน `mrp_app`

ทางเลือกถ้าไม่ต้องการเปิด public IP: ใช้ **Cloud SQL Studio** ใน console (login เป็น `mrp_owner`, database `postgres`)
แต่ Studio ไม่รองรับ psql variable และ `\gexec` ต้องแปลงเป็น SQL ตรงๆ และวางรหัสผ่านเอง:

```sql
CREATE ROLE mrp_app LOGIN PASSWORD '<ค่าจาก secret mrp-db-app-password>'
  NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
ALTER DATABASE mrp OWNER TO mrp_owner;
REVOKE ALL ON DATABASE mrp FROM PUBLIC;
GRANT CONNECT ON DATABASE mrp TO mrp_app;
SELECT rolname, rolsuper, rolbypassrls, rolcreaterole, rolcreatedb FROM pg_roles WHERE rolname = 'mrp_app';
```

สิ่งที่ทดสอบแล้ว / ยังไม่ได้ทดสอบ ของ `01-roles.sql`:

- ทดสอบแล้วกับ `postgres:17-alpine` ที่จำลอง role `cloudsqlsuperuser`: สร้าง role, รันซ้ำ, เปลี่ยนรหัสผ่าน, เปลี่ยน owner ของ database ผ่าน
- ขั้นที่ 3 ของ script (ถอน `cloudsqlsuperuser` ออกจาก `mrp_app` ในกรณีที่เคยสร้างผ่าน API) **ล้มเหลวในสภาพจำลอง**
  เพราะ `mrp_owner` ไม่มี ADMIN option — ถ้าเจอบน Cloud SQL จริงให้ลบ user นั้นด้วย `gcloud sql users delete` แล้วรัน script ใหม่
- **ยังไม่ได้รันกับ Cloud SQL จริง**

### 5. ตั้งค่า GitHub

สร้าง environment `staging` และ `production` ใน Settings > Environments แล้วใส่ **variables** ต่อ environment:

| Variable | ค่า |
|---|---|
| `GCP_PROJECT_ID` | project ID |
| `GCP_REGION` | `asia-southeast1` |
| `GCP_WORKLOAD_IDENTITY_PROVIDER` | `terraform output -raw wif_provider_name` |
| `GCP_DEPLOYER_SERVICE_ACCOUNT` | `terraform output -raw deployer_service_account_email` |
| `GAR_REPOSITORY` | `terraform output -raw registry_path` |
| `API_BASE_URL` | URL สาธารณะของ API (`terraform output -raw api_url` หรือ custom domain) |
| `NAME_PREFIX` | ไม่ต้องตั้งถ้าใช้ค่า default `mrp` |

Workflow ไม่ใช้ GitHub secret เลย — ยืนยันตัวตนด้วย Workload Identity Federation (ไม่มี JSON key)

สำหรับ `production` ให้ตั้ง **Required reviewers** (Settings > Environments > production) และจำกัด deployment branch เป็น `main`
ฝั่ง GCP รับ token เฉพาะ job ที่รันใน GitHub environment ชื่อตรงกับตัวแปร `environment` เท่านั้น
ดังนั้นชื่อ GitHub environment ต้องเป็น `staging` / `production` ตรงตัว (หรือกำหนดผ่าน `github_environment`)

### 6. Deploy ครั้งแรก

รัน workflow **Deploy** (Actions > Deploy > Run workflow) — จะ build image, รัน `mrp-migrate`, แล้ว deploy api + web

## งานประจำ

```sh
terraform init -reconfigure -backend-config=envs/<env>.backend.hcl
terraform plan -var-file=envs/<env>.tfvars -out=<env>.tfplan
terraform apply <env>.tfplan
```

- เปลี่ยน infra ผ่าน pull request เสมอ แนบผล `plan` ใน PR
- apply ที่ staging ก่อน production ทุกครั้ง
- production ต้องได้รับ approval ก่อน apply

## Ingress และ Cloudflare

ตอนนี้ `ingress = INGRESS_TRAFFIC_ALL` และให้ `allUsers` เรียกได้ หมายความว่า URL `*.run.app` เข้าถึงได้ตรงโดยไม่ผ่าน Cloudflare
(WAF / rate limit ของ Cloudflare ถูกข้ามได้) API ยังตรวจ JWT และมี rate limit ของตัวเอง

Cloud Run ไม่มี IP allowlist ในตัว วิธีจำกัดให้เข้าได้ผ่าน Cloudflare เท่านั้น เรียงจากถูกไปแพง:

1. **Shared secret header** — Cloudflare Transform Rule ใส่ header ลับ แล้วให้ API/web ปฏิเสธ request ที่ไม่มี header (ต้องแก้โค้ด, ไม่มีค่าใช้จ่าย)
2. **Cloudflare Tunnel** — ตั้ง ingress เป็น internal แล้วรัน `cloudflared` (ต้องมี VM/instance เปิดตลอด)
3. **External Application Load Balancer + Cloud Armor** — allowlist IP ของ Cloudflare, ตั้ง ingress เป็น `INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER`
   (มีค่า forwarding rule คงที่ต่อเดือน เกินงบช่วง 0–10 tenant)

แนะนำข้อ 1 ก่อนเปิดขายจริง

## Custom domain (optional)

ตั้ง `api_domain` / `web_domain` ใน tfvars แล้ว apply ขั้นตอน:

1. verify domain กับ Google (Search Console) ด้วย account ที่รัน Terraform
2. `terraform apply` แล้วดู `terraform output domain_dns_records`
3. เพิ่ม DNS record ใน Cloudflare แบบ **DNS only** จนกว่า Google จะออก certificate เสร็จ แล้วค่อยเปิด proxy (SSL mode: Full strict)
4. เพิ่ม origin ของ web ใน CORS — ถ้าไม่ได้ตั้ง `cors_origins` เอง Terraform จะเพิ่ม `https://<web_domain>` ให้อัตโนมัติ
5. อัปเดต GitHub variable `API_BASE_URL` แล้ว deploy ใหม่ (ค่า inline อยู่ใน web bundle)

Cloud Run domain mapping ยังเป็น preview และมี latency เพิ่มเล็กน้อย ถ้าไม่เหมาะ ให้ใช้ Cloudflare Worker/Origin Rule ชี้ไป `run.app` แทน
**ยังไม่ได้ทดสอบ**

## ประมาณการค่าใช้จ่าย

เป้าหมายตาม `docs/plan.md`: **USD 35–60/เดือน** ช่วง 0–10 tenant
ตัวเลขด้านล่างเป็นค่าประมาณ ต้องยืนยันกับ [Pricing Calculator](https://cloud.google.com/products/calculator) ก่อนเสนอราคา

| รายการ (ต่อ environment) | ประมาณ USD/เดือน |
|---|---|
| Cloud SQL `db-g1-small`, ZONAL | ~25–28 |
| SSD 10 GB + backup + transaction log (PITR) | ~2–5 |
| Cloud Run api + web (scale to zero, อยู่ใน free tier เป็นส่วนใหญ่) | ~0–5 |
| Cloud Run job migrate | ~0 |
| Artifact Registry (มี cleanup policy) | < 1 |
| Secret Manager (5 secrets) | < 1 |
| VPC, Direct VPC egress, private services access | 0 |
| **รวมต่อ environment** | **~30–40** |

**ข้อควรระวัง:** ถ้าเปิด staging และ production ด้วย `db-g1-small` ทั้งคู่ จะอยู่ที่ราว USD 60–80 **เกินเป้า**
ทางเลือกให้อยู่ในงบ:

- staging ใช้ `db_tier = "db-f1-micro"` (ราว USD 10) → รวมสอง environment ราว USD 45–55
- หรือหยุด instance ของ staging ช่วงที่ไม่ใช้ด้วย `db_activation_policy = "NEVER"` (จ่ายเฉพาะ storage)

ค่าใช้จ่ายที่จะเพิ่มเมื่อเปิดใช้: `api_min_instances = 1` (instance เปิดตลอด), worker (CPU always allocated), `REGIONAL` HA (ราว 2 เท่าของ instance)

Shared-core tier (`db-f1-micro`, `db-g1-small`) **ไม่อยู่ใน Cloud SQL SLA** — ควรย้ายเป็น `db-custom-2-7680` เมื่อมีลูกค้าจ่ายเงินและรับประกัน uptime 99.5%

ควรตั้ง Budget alert ใน Billing (เช่น 50% / 90% / 100% ของ USD 60) — ยังไม่ได้อยู่ใน Terraform นี้

## Connection pool

`db-g1-small` รับ connection ได้จำกัด ค่า default คือ `db_max_pool_size = 10` ต่อ instance และ `api_max_instances = 3`
รวมสูงสุด 30 + migrate job ถ้าเพิ่ม `api_max_instances` ต้องตรวจว่า `api_max_instances × db_max_pool_size` ยังต่ำกว่า `max_connections`

## Rollback

| สถานการณ์ | วิธี |
|---|---|
| Application revision ใหม่มีปัญหา | ไม่ใช้ Terraform — ย้าย traffic กลับ revision เดิม (ดู `infra/README.md`) |
| `terraform apply` ทำให้ config ผิด | `git revert` commit นั้น แล้ว `plan` + `apply` ใหม่ |
| State เสียหาย | กู้จาก object version ของ bucket: `gcloud storage ls -a gs://<bucket>/<prefix>/` แล้ว copy generation ก่อนหน้ากลับมา |
| ข้อมูลใน database เสียหาย | Point-in-time recovery ไปยัง instance ใหม่ (ดู `infra/README.md`) |

Terraform ตั้งใจไม่ลบสิ่งเหล่านี้: database `mrp` และ user `mrp_owner` (`deletion_policy = ABANDON`),
Cloud SQL instance และ Cloud Run service เมื่อ `deletion_protection = true` (บังคับสำหรับ production ด้วย precondition)

## หมุน secret

```sh
# JWT signing key (ผู้ใช้ทุกคนต้อง login ใหม่)
terraform apply -var-file=envs/<env>.tfvars -replace=random_password.jwt_signing_key

# รหัสผ่าน mrp_app — หลัง apply ต้องรัน bootstrap-db-roles.sh ตามขั้นตอนข้อ 4 ทันที
terraform apply -var-file=envs/<env>.tfvars -replace=random_password.db_app
```

Cloud Run อ่าน secret version `latest` ตอน instance start ดังนั้นหลังหมุน secret ต้อง deploy revision ใหม่ (รัน workflow Deploy)
ระหว่างหมุนรหัสผ่าน `mrp_app` จะมีช่วงสั้นๆ ที่ instance เดิมต่อ database ไม่ได้ ให้ทำนอกเวลาใช้งาน

## Worker

`worker_enabled = false` — API ยังไม่มี worker mode ถ้าเปิดตอนนี้จะได้ API อีกชุดหนึ่งเท่านั้น
เมื่อมี `dotnet Mrp.Api.dll worker` แล้ว: ตรวจ `args` ใน `run.tf`, ตั้ง `worker_enabled = true`, และเพิ่มขั้นตอน deploy worker ใน workflow

## ตรวจสอบ config

```sh
terraform fmt -check -recursive
terraform init -backend=false
terraform validate

# ไม่มี Terraform ในเครื่อง
docker run --rm -v "$PWD":/work -w /work hashicorp/terraform:1.13 validate
```
