# Specs ของระบบใหม่

เขียน 1 ไฟล์ต่อ flow ก่อนเริ่มโค้ด อ้างอิง `docs/legacy/logic/<flow>.md` แล้วออกแบบใหม่ (ไม่ต้องเหมือนเดิม แต่ต้องครอบคลุมกฎธุรกิจเดิม)

Template ต่อไฟล์:
1. Scope + แพ็กเกจที่ใช้
2. Entities (ตาราง Postgres, schema, คอลัมน์สำคัญ, `tenant_id`, index)
3. State machine (สถานะ, transition, ใครทำได้, ขั้นอนุมัติเป็น tenant setting อย่างไร)
4. กฎคำนวณ / validation (พร้อม golden test case)
5. API endpoints (REST, OpenAPI)
6. หน้าจอ: web (path, component) / mobile (screen, offline?)
7. Tenant settings ที่เกี่ยว
8. สิ่งที่ตัดจากระบบเดิมและเหตุผล
