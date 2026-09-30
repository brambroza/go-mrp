# Spec: Platform core (tenant, identity, สิทธิ์, เลขเอกสาร, กล่องอนุมัติ)

อ้างอิง `docs/legacy/logic/platform.md` · โมดูล api: `Mrp.Platform` · schema: `platform`

## 1. Scope + แพ็กเกจ

Core (ทุกแพ็ก): tenant, ผู้ใช้, role/permission, login (JWT + refresh), 2FA (TOTP) สำหรับผู้อนุมัติ,
เลขเอกสารต่อ tenant ต่อประเภท, approval engine กลาง + กล่องอนุมัติ, tenant settings

## 2. Entities

ทุกตาราง (ยกเว้น `tenants`) มี `tenant_id uuid not null` + RLS policy `tenant_isolation`

| ตาราง | คอลัมน์สำคัญ | index / constraint |
|---|---|---|
| `platform.tenants` | id, slug, name, plan (`Starter`/`Pro`/`Enterprise`), status (`Trial`/`Active`/`Suspended`), time_zone, default_language, max_users | unique(slug) · **ไม่มี RLS** (ใช้ resolve tenant ตอน login) |
| `platform.users` (Identity) | id, tenant_id, user_name, email, password_hash, display_name, language, is_active, two_factor_enabled | unique(tenant_id, normalized_user_name), unique(tenant_id, normalized_email) |
| `platform.roles` (Identity) | id, tenant_id, name, is_system | unique(tenant_id, normalized_name) |
| `platform.role_claims` | role_id, claim_type=`perm`, claim_value=รหัสสิทธิ์ | |
| `platform.user_roles` | user_id, role_id | |
| `platform.refresh_tokens` | id, tenant_id, user_id, token_hash (SHA-256), expires_at, revoked_at, replaced_by_id, device | index(token_hash) |
| `platform.tenant_settings` | tenant_id, key, value (jsonb) | unique(tenant_id, key) |
| `platform.document_number_formats` | tenant_id, document_type, prefix, period (`None`/`Year`/`Month`), digits, separator | unique(tenant_id, document_type) |
| `platform.document_sequences` | tenant_id, document_type, period_key, last_number | unique(tenant_id, document_type, period_key) |
| `platform.approval_routes` | tenant_id, document_type, name, is_active | unique(tenant_id, document_type) where is_active |
| `platform.approval_route_steps` | route_id, step_no, name, role_id, min_amount (numeric 18,4, null = ทุกยอด), require_two_factor | unique(route_id, step_no) |
| `platform.approval_requests` | tenant_id, document_type, document_id, document_no, title, amount, requested_by, status, current_step_no, total_steps | index(tenant_id, status), unique(document_type, document_id) where status = `Pending` |
| `platform.approval_request_steps` | request_id, step_no, name, role_id, require_two_factor — **snapshot** ของ step ที่ใช้กับเอกสารนี้ ณ ตอน submit (แก้ route ภายหลังไม่กระทบคำขอที่ค้างอยู่) | unique(request_id, step_no) |
| `platform.approval_actions` | request_id, step_no, action (`Approve`/`Reject`/`Withdraw`), acted_by, acted_at, comment | |

## 3. State machine

**ApprovalRequest:** `Pending → Approved` (ผ่านขั้นสุดท้าย) · `Pending → Rejected` (ขั้นใดก็ได้) · `Pending → Withdrawn` (ผู้ขอถอนเอง)

- ขั้นอนุมัติเป็น config ต่อ tenant ต่อประเภทเอกสาร (`approval_routes`) — DK เดิม 2 ขั้น (หัวหน้า → ผู้จัดการ) กลายเป็น route ตั้งต้นที่แก้ได้
- ไม่มี route active สำหรับประเภทนั้น → อนุมัติอัตโนมัติ (0 ขั้น)
- step ที่มี `min_amount` มากกว่ายอดเอกสาร → ข้าม step นั้น
- ผู้อนุมัติ = ผู้ใช้ที่มี role ของ step ปัจจุบัน; ผู้ขอ**ห้าม**อนุมัติเอกสารตัวเอง (ตั้งค่า `approval.allowSelfApprove` = false เป็นค่าตั้งต้น)
- step ที่ `require_two_factor` → ผู้อนุมัติต้องเปิด 2FA และ token ต้องมี claim `amr=mfa`
- เมื่อ request จบ (Approved/Rejected/Withdrawn) engine เรียก `IApprovalSubscriber` ของโมดูลเจ้าของเอกสารภายใน transaction เดียวกัน เพื่อเปลี่ยนสถานะเอกสาร

**User:** `Active ↔ Inactive` (ไม่ลบจริง) · **Tenant:** `Trial → Active → Suspended → Active`

## 4. กฎ / validation

- เลขเอกสาร = `prefix + sep + period + sep + running` เช่น `PO-2610-0001`; period ใช้เวลา Asia/Bangkok (หรือ time zone ของ tenant); running reset เมื่อ period เปลี่ยน
  ออกเลขด้วย `INSERT … ON CONFLICT DO UPDATE … RETURNING` ภายใน transaction ของเอกสาร → ไม่ซ้ำ, rollback แล้วเลขไม่หาย
- รหัสผ่าน: ≥ 8 ตัว มีตัวเลข + ตัวอักษร; hash ด้วย Identity (PBKDF2); ล็อก 15 นาทีหลังผิด 5 ครั้ง
- access token 15 นาที, refresh token 30 วัน หมุนทุกครั้งที่ใช้; ใช้ refresh token ที่ถูกหมุนไปแล้วซ้ำ → revoke ทั้งสายของ user นั้น
- จำนวนผู้ใช้ active ≤ `tenants.max_users` ของแพ็ก
- permission เป็นรหัสคงที่ในโค้ด (`purchasing.po.approve`) ผูกกับ role ต่อ tenant; role ระบบ `Owner` ได้ทุกสิทธิ์และลบไม่ได้

Golden test: numbering (พร้อมกัน 50 request ต้องได้เลข 1..50 ไม่ซ้ำ), approval (2 ขั้น, ข้ามขั้นตามยอด, ห้ามอนุมัติตัวเอง, reject)

## 5. API

| Method | Path | สิทธิ์ |
|---|---|---|
| POST | `/api/v1/auth/signup` | public (สร้าง tenant + owner) |
| POST | `/api/v1/auth/login` · `/refresh` · `/logout` | public |
| GET | `/api/v1/auth/me` | login |
| POST | `/api/v1/auth/2fa/setup` · `/2fa/enable` · `/2fa/disable` | login |
| GET/POST/PUT | `/api/v1/users` | `platform.users.manage` |
| GET/POST/PUT | `/api/v1/roles` · GET `/api/v1/permissions` | `platform.roles.manage` |
| GET/PUT | `/api/v1/settings` | `platform.settings.manage` |
| GET/PUT | `/api/v1/document-number-formats` | `platform.settings.manage` |
| GET/PUT | `/api/v1/approval-routes` | `platform.approvals.configure` |
| GET | `/api/v1/approvals/inbox` · `/api/v1/approvals/{id}` | login |
| POST | `/api/v1/approvals/{id}/approve` · `/reject` · `/withdraw` | ตาม role ของ step |

Error format: RFC 7807 `application/problem+json` + `code` (รหัสข้อความ i18n)

## 6. หน้าจอ

- Web: `/login`, `/signup`, `/settings/users`, `/settings/roles`, `/settings/approval-routes`, `/settings/numbering`, `/approvals`
- Mobile: login, กล่องอนุมัติรวม (approve/reject + comment), ตั้งค่า 2FA — ต้อง online

## 7. Tenant settings

`approval.allowSelfApprove` (bool), `locale.timeZone`, `locale.defaultLanguage`, `inventory.*`, `production.*` (ดู spec ของโมดูลนั้น)

## 8. สิ่งที่ตัดจากระบบเดิม

- สิทธิ์ระดับปุ่มต่อฟอร์ม → เปลี่ยนเป็น permission ต่อ action ของ API
- ภาษาในตาราง `MSysLanguage` → ไฟล์ i18n ฝั่ง web/mobile
- dynamic master screen → หน้าจอ master ปกติ
