# QueueSystem พร้อม SQLite

## รันบน Windows

ใช้ .NET 8 SDK เปิด PowerShell ในโฟลเดอร์ที่มี `QueueSystem.csproj` แล้วรัน

```powershell
dotnet run --project .\QueueSystem.csproj --urls http://localhost:5080
```

เปิด http://localhost:5080/ สำหรับรับบัตรคิว และ http://localhost:5080/staff สำหรับหน้าเจ้าหน้าที่  
ระบบสร้าง `data/queues.db` และข้อมูลสมมติเมื่อเปิดใช้ฐานข้อมูลว่าง ครั้งต่อไปจะอ่านคิวเดิมจากไฟล์นี้

## รันด้วย Docker

Dockerfile ใช้ .NET 10 สำหรับ Linux container ใช้ Docker เครื่องเดียวและรันแอปหนึ่ง instance เพื่อให้การอัปเดตหน้าจอแบบสดใช้กระบวนการเดียวกัน

```bash
docker build -t queue-system:sqlite .
docker volume create queue-system-data
docker run -d --name queue-system-db --restart unless-stopped -p 127.0.0.1:5081:10000 -v queue-system-data:/app/data queue-system:sqlite
```

เข้าจากเครื่องที่รัน Docker ที่ http://localhost:5081/ และ http://localhost:5081/staff  
หากมี reverse proxy บนเครื่องเดียวกัน ให้ชี้ไปที่พอร์ต 5081 และรองรับ WebSocket
เมื่อติดตั้งเวอร์ชันใหม่ ให้ mount volume ชื่อเดิมที่ /app/data เพื่อใช้ฐานข้อมูลเดิม
เวอร์ชันนี้ใช้ในแล็บหรือเครือข่ายที่จำกัดผู้เข้าถึง หน้าเจ้าหน้าที่และ API จัดการคิวยังไม่มีล็อกอิน

## ตรวจผล

1. รับบัตรคิวพร้อมใส่ชื่อหรือหมายเหตุที่จำได้
2. เปิดหน้าเจ้าหน้าที่ เลือกวันที่และประเภทบริการ ตรวจว่าพบคิวดังกล่าว
3. หยุดแล้วเปิดเว็บใหม่ กลับไปดูคิวเดิมและสถานะเดิม
4. เลือกวันที่อื่น ระบบจะแสดงเฉพาะคิวของวันที่นั้น

ฐานข้อมูลใช้ตาราง `queue_tickets` เลขคิวเพิ่มต่อเนื่องแยกตามประเภทบริการ
คิวจากเวอร์ชันที่เก็บใน RAM จะไม่ถูกย้ายเข้า SQLite อัตโนมัติ
