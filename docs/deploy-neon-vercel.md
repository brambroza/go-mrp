# แผน deploy demo: Neon + Vercel (+ ที่รัน API)

เขียน 2026-09-30 · สถานะ: **แผน ยังไม่ได้ทำ** · ใช้แทน `docs/deploy-demo.md` ในส่วนของ web
ต้องได้รับอนุมัติจากเจ้าของก่อน deploy (กฎ CLAUDE.md) · CLAUDE.md เดิมระบุ "ไม่ใช้ Vercel" — เจ้าของขอเปลี่ยนสำหรับ demo เมื่อ 2026-09-30

## ข้อจำกัดที่ต้องรู้ก่อน

1. **Vercel รัน API .NET ไม่ได้** — Vercel รันได้เฉพาะ Node/serverless. API ต้องไปอยู่ที่อื่น (ตารางด้านล่าง)
2. **Vercel แผนฟรี (Hobby) ห้ามใช้เชิงพาณิชย์** ตามเงื่อนไขที่ผมรู้ — demo ให้ลูกค้าดูอาจถือว่าเชิงพาณิชย์ ต้อง**ตรวจเงื่อนไขปัจจุบัน**; ถ้าไม่ผ่านต้องใช้แผน Pro (มีค่าใช้จ่าย)
3. **Neon ต้องใช้ host แบบ direct** (ไม่มี `-pooler`) เพราะการแยกบริษัทพึ่งค่า session — `scripts/migrate-demo.sh` ตรวจข้อนี้ให้
4. Free tier ทุกเจ้าหลับเมื่อไม่ใช้ → คำขอแรกช้า 5–20 วินาที; **ห้ามเก็บข้อมูลจริงของลูกค้า** (ไม่มี backup/SLA)
5. เงื่อนไข free tier ทั้งหมดในไฟล์นี้**ยังไม่ได้ตรวจกับหน้าราคาปัจจุบัน** (ค้นเว็บไม่ได้ใน session ที่เขียน)

## โครงสร้าง

```
ลูกค้า ──► Vercel (web Next.js)  ──► API (.NET container)  ──► Neon (PostgreSQL 17, Singapore)
                │ proxy /api/v1/* ไปหา API                       ▲
                └── cookie refresh token ที่ web เอง              └── migrate รันจากเครื่อง dev หรือ CI
LINE / มือถือ ────────────────────────► API โดยตรง
```

Web เรียก API ผ่าน proxy ของตัวเอง (ตั้งค่า `API_BASE_URL` ฝั่ง server) → ไม่ต้องตั้ง CORS ของ API ให้ Vercel และ token ไม่โผล่ใน browser

### ที่รัน API — เลือก 1 ทาง

| ทาง | ฟรี? | ข้อดี | ข้อเสีย |
|---|---|---|---|
| **Google Cloud Run** (แนะนำ) | โควตาฟรีรายเดือน (ต้องผูกบัตร) | มี Dockerfile + Terraform + workflow อยู่แล้ว; ย้ายขึ้น production ได้เลย | ต้องสร้าง GCP project |
| Koyeb / Render free instance | ฟรีแบบจำกัด | ไม่ต้องผูกบัตร (ต้องตรวจ) | หลับนาน, RAM น้อย, ไม่มีของเดิมรองรับ |
| Fly.io | มีเครดิตฟรีจำกัด (ต้องตรวจ) | region Singapore | ต้องเขียน config ใหม่ |

แผนนี้เขียนตาม Cloud Run; ทางอื่นเปลี่ยนแค่ขั้น C

## งานที่ต้องเสร็จก่อน deploy

| # | งาน | สถานะ |
|---|---|---|
| 1 | ฐานข้อมูล dev บนเครื่องกลับมาใช้ได้ (หลังดิสก์เต็ม) | ยัง |
| 2 | `dotnet test` ผ่านทั้งหมด (143 + 44) | ต้องรันซ้ำ |
| 3 | `.env.demo` มีค่าจริง → `bash scripts/setup-demo-env.sh` แล้ว `bash scripts/migrate-demo.sh` ผ่าน | ยัง (script ยังไม่เคยรัน) |
| 4 | Web e2e ผ่านกับ API จริง (`pnpm --filter @mrp/web e2e`) | ยัง — web ยังไม่เคย login กับ API จริงสำเร็จ |
| 5 | แก้ API: แยก rate limit ของ `/auth/refresh`, เชื่อ `X-Forwarded-For` เฉพาะ proxy | ยัง |
| 6 | ปิด signup สาธารณะบน demo (config `Signup:Enabled=false`) และสร้างบริษัท demo ด้วย script | ยัง |
| 7 | หน้า web ของ flow ที่จะ demo (13 หน้า ตาม `docs/deploy-demo.md`) | ยัง |
| 8 | Script ข้อมูลตัวอย่าง | ยัง |
| 9 | Security review + code review | ยัง |
| 10 | git commit + GitHub repo (private) | ยัง — ต้องอนุมัติก่อน push |

## ขั้นตอน deploy

### A. Neon (ทำแล้วบางส่วน)

1. Reset รหัส `neondb_owner` (รหัสเดิมหลุดในแชต) — **ต้องทำ**
2. Copy connection string → `bash scripts/setup-demo-env.sh` (สร้าง role `mrp_app`, เขียน `.env.demo`, ล้าง clipboard)
3. `bash scripts/migrate-demo.sh` → ต้องเห็น `Row-Level Security applied ...` และผลตรวจ 4 ข้อผ่าน
4. ตั้ง Neon ให้ไม่หลับเร็วเกิน (ถ้าแผนฟรีให้ตั้งได้) เพื่อลดอาการช้าครั้งแรก

### B. GitHub

1. `git add -A && git commit` (ยังไม่เคย commit; ตรวจว่า `.env*` ไม่ติดไป: `git status --ignored | grep env`)
2. สร้าง repo private → `git push` (ขออนุมัติก่อน)
3. ตั้ง GitHub Actions secrets/variables ตามหัวไฟล์ `.github/workflows/deploy.yml` (ถ้าจะให้ CI deploy API)

### C. API บน Cloud Run

1. สร้าง GCP project, เปิด Cloud Run + Artifact Registry + Secret Manager, ตั้ง budget alert USD 5
2. Secret 3 ตัว: `ConnectionStrings__App` (mrp_app, host direct), `ConnectionStrings__Migrator`, `Jwt__SigningKey` — ค่าเดียวกับ `.env.demo`
3. Build/push image: `docker build --platform linux/amd64 -t asia-southeast1-docker.pkg.dev/<project>/mrp/api:<sha> api/` แล้ว push
4. Deploy: region `asia-southeast1`, port 8080, min instances 0, max 2, memory 512Mi, env `ASPNETCORE_ENVIRONMENT=Production`, `Cors__Origins__0=https://<web>.vercel.app`, `Signup__Enabled=false`
   - API จะปฏิเสธการเริ่มทำงานถ้า role ที่ต่อเป็น superuser/BYPASSRLS — ถ้าเจอ error นี้แปลว่าใส่ connection string ผิดตัว
5. ตรวจ `https://<api>.run.app/healthz` = `Healthy`
6. (ทางเลือก) ใช้ Terraform ใน `infra/terraform` โดยปิดส่วน Cloud SQL — ยังไม่มีตัวแปรนี้ ต้องเพิ่ม

### D. Web บน Vercel

1. Import repo ใน Vercel → Root Directory = `web`, Framework = Next.js, Install Command = `pnpm install --frozen-lockfile` (รันจาก root ของ monorepo — ตั้ง "Include files outside root directory" ให้ Vercel เห็น `packages/api-client`)
2. Environment variables (Production):
   - `API_BASE_URL=https://<api>.run.app` (ใช้โดย proxy ฝั่ง server)
   - `COOKIE_SECURE=true`
   - ไม่ตั้ง `NEXT_PUBLIC_API_BASE_URL` (ถ้าตั้ง web จะเรียก API ตรงและต้องตั้ง CORS)
   - ดูชื่อตัวแปรทั้งหมดใน `web/.env.example`
3. `output: "standalone"` ใน `next.config` ใช้กับ Vercel ได้ (Vercel ข้าม) — ไม่ต้องแก้
4. Deploy → เปิด `https://<web>.vercel.app/login`
5. กลับไปตั้ง `Cors__Origins__0` ของ API เป็น URL ของ Vercel (เผื่อมีการเรียกตรงจากมือถือ/LIFF ในอนาคต)

### E. หลัง deploy

1. รัน script ข้อมูลตัวอย่าง (สร้างบริษัท demo, ผู้ใช้ 3 บทบาท, สินค้า, BOM, สต็อก, PO)
2. ทดสอบด้วยมือ: login → PO → อนุมัติ → รับของ → MRP → ใบสั่งผลิต ทั้งบน desktop และมือถือ
3. ทดสอบ "หลับแล้วตื่น": ทิ้งไว้ 30 นาที แล้วเปิดใหม่ ดูว่าคำขอแรกไม่ error (แค่ช้า)
4. ส่งลิงก์ให้ลูกค้าพร้อมบัญชีทดลอง แจ้งข้อจำกัด (ยังไม่มี SO/Gantt/invoice, ข้อมูลอาจถูกล้าง)

## Rollback

- Vercel: กด "Promote" deployment ก่อนหน้า
- Cloud Run: ย้าย traffic ไป revision ก่อนหน้า
- Neon: ใช้ branch ของ Neon ก่อน migrate ครั้งถัดไป (สร้าง branch = snapshot ฟรี) — ถ้าพัง restore จาก branch

## ค่าใช้จ่ายโดยประมาณ (ต้องตรวจ)

Neon ฟรี + Cloud Run ในโควตาฟรี + Vercel Hobby ฟรี (ถ้าเงื่อนไขอนุญาต) = 0 บาท/เดือน สำหรับ demo ที่มีคนใช้น้อย
ถ้า Vercel Hobby ใช้ไม่ได้: Vercel Pro ประมาณ USD 20/คน/เดือน หรือย้าย web ไป Cloud Run ตามแผนเดิม (Dockerfile มีแล้วแต่ยังไม่ผ่าน)
