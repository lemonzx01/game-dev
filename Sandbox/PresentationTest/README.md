# PresentationTest Sandbox (Isolated Mock Environment)

> **คำเตือนสำคัญ:** โฟลเดอร์ `Sandbox/PresentationTest/` เป็น **พื้นที่ทดสอบแยกต่างหากสำหรับ UI, Graphics, Modular Character, Camera/Zoom และ Sprite Optimization เท่านั้น**
>
> - ใช้ **Mock Data ชั่วคราว** (`MockData/MockWorldData.cs`) เท่านั้น — **ไม่ใช่ Production Code**
> - **ไม่มี** Simulation Engine, World Rules, AI Decision, Pathfinding หรือ Save/Load
> - **ไม่เชื่อมต่อ** `ISimulation` หรือ `T001` Contract ที่ยังไม่อนุมัติ
> - ห้ามย้ายไฟล์ใน `MockData/` เข้าไปใน `Game/` หรือ `Simulation/` จริง
