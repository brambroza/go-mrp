# MRP SaaS

ERP สำหรับโรงงาน SME ไทย แบบ SaaS multi-tenant — web (งานออฟฟิศ) + mobile (งานหน้างานและอนุมัติ)
กติกาโปรเจกต์อยู่ใน [`CLAUDE.md`](CLAUDE.md) · แผนธุรกิจและ roadmap อยู่ใน [`docs/plan.md`](docs/plan.md) · สถานะงานล่าสุดอยู่ใน [`docs/STATUS.md`](docs/STATUS.md)

## โครงสร้าง

| โฟลเดอร์ | เนื้อหา |
|---|---|
| `api/` | .NET 10 modular monolith: `Mrp.SharedKernel`, โมดูล `Platform`, `Masters`, `Inventory`, `Purchasing`, `Production`, host `Mrp.Api` |
| `web/` | Next.js (App Router) + TypeScript + Tailwind + shadcn/ui |
| `mobile/` | Expo (React Native) + TypeScript |
| `packages/api-client/` | TypeScript client ที่ generate จาก OpenAPI ของ API |
| `infra/` | Dockerfile, Terraform (GCP Singapore), script สร้าง role ของ Postgres |
| `docs/specs/` | spec ของระบบใหม่ ต่อ flow |
| `docs/legacy/` | business logic ที่ถอดจากระบบเดิม (อ่านอย่างเดียว) |

## เริ่มใช้งานบนเครื่อง

ต้องมี: Docker, .NET SDK 10 (ดู `api/global.json`), Node 22+, pnpm 11

```sh
cp .env.example .env            # แล้วตั้งรหัสผ่านและ JWT_SIGNING_KEY เอง (ห้าม commit .env)
docker compose up -d postgres   # PostgreSQL 17 ที่พอร์ต 5433
. scripts/dev-env.sh            # export connection string จาก .env + ใช้ .NET 10

cd api
dotnet run --project src/Mrp.Api -- migrate   # สร้าง schema + เปิด Row-Level Security
dotnet run --project src/Mrp.Api              # http://localhost:5080  (OpenAPI: /openapi/v1.json, health: /healthz)
```

```sh
pnpm install
pnpm web:dev        # http://localhost:3000
pnpm mobile:start
```

สมัครบริษัทแรกที่หน้า `/signup` ของ web หรือ `POST /api/v1/auth/signup`

### เมื่อ API เปลี่ยน

```sh
pnpm api:openapi    # ดึง OpenAPI จาก API ที่รันอยู่ แล้ว generate packages/api-client/src/schema.d.ts
```

### เพิ่ม migration

```sh
. scripts/dev-env.sh && cd api
dotnet ef migrations add <Name> --project src/Modules/Mrp.<Module> --startup-project src/Mrp.Api \
  --context <Module>DbContext -o Persistence/Migrations
```

ตารางใหม่ที่มีคอลัมน์ `tenant_id` จะถูกเปิด RLS อัตโนมัติเมื่อรัน `migrate` (ดู `RlsBootstrapper`)

## ทดสอบ

```sh
. scripts/dev-env.sh && cd api
dotnet test                                   # domain (golden test สูตร) + integration (ต้องมี Docker)
dotnet test --collect:"XPlat Code Coverage"   # coverage
```

Integration test ใช้ Testcontainers เปิด PostgreSQL 17 จริง สร้าง role `mrp_owner` / `mrp_app` แล้วรัน API เป็น role ที่ติด RLS
เหมือน production จึงตรวจการแยกข้อมูลระหว่าง tenant ที่ระดับฐานข้อมูลได้จริง

## หลักสำคัญของระบบ (อ่านก่อนแก้โค้ด)

- **แยก tenant 2 ชั้น:** EF global query filter + PostgreSQL Row-Level Security (`SET app.tenant_id`) — API ต่อฐานข้อมูลด้วย role `mrp_app`
  ที่ไม่ใช่ superuser และไม่มี BYPASSRLS; migration รันด้วย `mrp_owner`
- **tenant มาจาก JWT เท่านั้น** ไม่รับจาก header/query/body
- **transaction เดียวข้ามโมดูล:** ทุก DbContext ใช้ connection เดียวกันต่อ request (`DbSession`) เอกสาร + ledger + เลขเอกสาร + ผลอนุมัติ commit/rollback พร้อมกัน
- **สต็อกเป็น ledger append-only** (`inventory.movements`) ห้ามแก้/ลบที่ระดับ trigger ยอดคงเหลือเป็น cache ที่อัปเดตใน transaction เดียวกัน
- **เลขเอกสาร** ออกจาก sequence ต่อ tenant ต่อประเภท ภายใน transaction ของเอกสาร
- **ขั้นอนุมัติ** เป็น route ต่อ tenant ต่อประเภทเอกสาร ใน approval engine กลาง
- **วันที่/ตัวเลข:** เงิน `numeric(18,4)` ปริมาณ `numeric(18,6)` เวลา `timestamptz` (UTC) — ฝั่ง server ใช้ invariant culture เสมอ
  (เครื่องที่ตั้ง locale ไทยจะได้ปี พ.ศ. ถ้า format ตาม culture)
- **ของเฉพาะโรงงาน DK** (mix/fill, อนุมัติ 2 ขั้น, ปฏิทิน 2 กะ) เป็นค่าตั้งต่อ tenant ไม่ hard-code
