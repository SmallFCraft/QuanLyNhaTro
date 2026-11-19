# Design — Sprint M1+M2: Nền tảng TCP/RBAC + Khu–Phòng–Người thuê

**Ngày:** 2026-11-20 · **Repo:** `D:\Codes\QuanLyNhaTro` · **Nguồn:** `docs/KEHOACH_THUC_THI_DE_TAI.md` (22 mục, đã duyệt)

## Intent

Dựng thật khung dự án chạy được ổn định: đăng nhập 6 vai trò qua TCP + RBAC server-side (M1), quản lý khu trọ/phòng/người thuê theo scope khu (M2). Kết thúc sprint: client chạy end-to-end, test xanh.

## Goals

- Solution .NET 8: `QuanLyTro` (WinForms+WebView2 client) · `QuanLyTro.Server` (console TCP 8888) · `QuanLyTro.Shared` (protocol/models) · `QuanLyTro.Tests` (MSTest + MySQL test DB `quanly_nhatro_test`).
- Giao thức: JSON-per-line, `RequestPacket/ResponsePacket` bắt buộc `requestId`, giới hạn frame 256KB, semaphore ghi socket, SessionStore + lockout 5 sai/1 phút, PBKDF2 600k vòng.
- RBAC deny-by-default: `ActionNames` + `DefaultPermissions` + router kiểm tra trước service; QuanLy scope `khu_tro_id` lấy từ session (không tin payload); CongAn chỉ đọc.
- Schema M1+M2: `tai_khoan`, `quyen_vai_tro`, `quyen_mac_dinh_da_ap_dung`, `cau_hinh_he_thong`, `khu_tro`, `phong`, `ho_so_khach_thue`, `lich_su_cu_tru` — SchemaInitializer idempotent.
- Nghiệp vụ M2: CRUD khu/phòng, phân công quản lý (1 quản lý = 1 khu), đăng ký public khách thuê (CCCD 12 số unique), chuyển/trả phòng nguyên tử ghi `lich_su_cu_tru`, tìm kiếm ≤50 kết quả.
- UI: Form1 host WebView2, asset host `https://app.quanlytro.local`, login + 6 shell (M1 empty state; M2 nội dung khu/phòng/người thuê cho ChuTro/QuanLy/KhachThue/CongAn).
- Tests: schema idempotent, RBAC (giả mạo vai trò/scope), lockout, TCP framing (fragmented/multi-packet/oversize), đua sức chứa phòng, rollback đăng ký trùng CCCD. **Toàn bộ xanh trước khi kết thúc.**

## Non-goals (nhịp sau)

Hợp đồng/QR/PIN/phụ lục (M3), điện nước/hóa đơn/đề xuất/sự cố (M4), lịch/thống kê/lưu trú công an (M5).

## Approaches đã xem xét

1. **Dựng mới sạch theo kế hoạch 22 mục** — code server/network/security port pattern kiểm chứng từ dự án cũ `D:\Dai Hoc\LapTrinhMang` (242 test xanh) nhưng viết lại vào solution mới với tên/model hợp nhất `tai_khoan` 1 bảng. ✅ **Chọn** — sạch, đúng 6 vai trò, không kéo nợ thiết kế cũ.
2. Copy nguyên dự án cũ rồi sửa — nhanh nhưng kế thừa 3 bảng đăng nhập cũ + model lệch kế hoạch, phải refactor ngược. ❌
3. Chỉ dựng khung không test — vi phạm tiêu chí "chạy ổn định". ❌

## Constraints

- MySQL Laragon (`root`, không password dev) — user phải bật MySQL trước khi chạy test integration.
- Không đăng nhập/khóa qua client trực tiếp DB; mọi query parameterized.
- Commit không attribution; tên entity không dấu (ChuTro, QuanLy...); DB test chỉ `quanly_nhatro_test`.

## Đã quyết

Kiến trúc, tech stack, model dữ liệu, phân công — theo kế hoạch 22 mục đã duyệt. Còn lại cho builder: chi tiết implementation từng task.

## Ghi chú môi trường

- SDK 10.0.404 + runtime 8.0.34 → target `net8.0.0` OK.
- MySQL 8.0.30 đang tắt — cần start trước bước chạy test.
