# QuanLyNhaTro — Hệ thống quản lý phòng trọ đa khu phường Ngũ Hành Sơn

Dự án môn Lập trình mạng: client-server TCP, 6 vai trò, MySQL 8.0.

## Kiến trúc

| Project | Vai trò |
|---|---|
| `QuanLyTro/QuanLyTro.Client` | WinForms .NET 8 + WebView2, giao diện HTML/CSS/JS cục bộ |
| `QuanLyTro/QuanLyTro.Server` | Console .NET 8, TCP listener cổng 8888, toàn bộ nghiệp vụ |
| `QuanLyTro/QuanLyTro.Shared` | Protocol packet, ActionNames, DefaultPermissions, DTO |
| `QuanLyTro/QuanLyTro.Tests` | MSTest — 44 test (schema, RBAC, TCP, nghiệp vụ, E2E) |

Giao thức: JSON một dòng, bắt buộc `requestId`, deny-by-default RBAC tại server, PBKDF2 600k vòng, lockout 5 sai/1 phút.

## Cách chạy

1. **Bật MySQL** (Laragon) — nếu MySQL chưa chạy:
   ```
   E:\Apps\laragon\bin\mysql\mysql-8.0.30-winx64\bin\mysqld.exe --defaults-file=E:\Apps\laragon\bin\mysql\mysql-8.0.30-winx64\my.ini
   ```
   (hoặc mở Laragon → Start All)

2. **Chạy server** (tự tạo DB `quanly_nhatro` + schema + seed tài khoản demo lần đầu):
   ```bash
   dotnet run --project QuanLyTro/QuanLyTro.Server
   ```

3. **Chạy client:**
   ```bash
   dotnet run --project QuanLyTro/QuanLyTro.Client
   ```

4. **Dữ liệu mẫu** (khu A/B, phòng, cấu hình — tùy chọn):
   ```bash
   E:\Apps\laragon\bin\mysql\mysql-8.0.30-winx64\bin\mysql.exe -h 127.0.0.1 -u root < Database/seed_demo.sql
   ```

## Tài khoản demo

| Tài khoản | Mật khẩu | Vai trò |
|---|---|---|
| `chutro` | `chutro` | Chủ trọ (toàn quyền) |
| `quanly` | `quanly` | Quản lý (chỉ khu được gán) |
| `letan` | `letan` | Lễ tân (tra cứu phòng) |
| `nhanvien` | `nhanvien` | Nhân viên |
| Khách thuê | — | tự đăng ký bằng CCCD qua màn hình đăng nhập |

## Chạy test

Test integration dùng riêng DB `quanly_nhatro_test` — MySQL phải đang chạy:

```bash
dotnet test QuanLyTro/QuanLyTro.Tests
```

E2E qua TCP thật (`RealServerE2E`) yêu cầu server đang chạy ở cổng 8888.

## Roadmap

- **M1 ✅** Nền tảng TCP/RBAC/tài khoản — xong
- **M2 ✅** Khu trọ/phòng/người thuê + scope khu — xong
- **M3** Hợp đồng, QR/PIN nhận phòng, phụ lục
- **M4** Điện nước, hóa đơn, đề xuất duyệt, sự cố
- **M5** Lịch kiểm tra, thống kê, lưu trú công an
