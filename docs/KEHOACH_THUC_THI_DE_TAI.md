# KẾ HOẠCH THỰC THI ĐỀ TÀI
# HỆ THỐNG QUẢN LÝ PHÒNG TRỘ PHƯỜNG NGŨ HÀNH SƠN
# Quản lý nhà trọ đa khu — TCP Client-Server — 6 vai trò

**Dự án phần mềm nhóm — Đồ án học phần**
**Quy mô nhóm: 5 sinh viên**
**Đà Nẵng, tháng 11 năm 2026**

---

## MỤC LỤC

- I. Thông tin tổng quan
- II. Vấn đề cần giải quyết
- III. Giải pháp đề xuất
- IV. Mục tiêu cụ thể (6 phân hệ chức năng)
- V. Đối tượng sử dụng (6 vai trò)
- VI. Công nghệ sử dụng (TCP Client-Server)
- VII. Công nghệ phần mềm
- VIII. Kiến trúc hệ thống
- IX. Thiết kế dữ liệu (11 bảng)
- X. Thiết kế giao thức TCP & phân quyền RBAC
- XI. Quy trình nghiệp vụ chính
- XII. Quy trình nhận phòng QR/PIN
- XIII. Thiết kế giao diện
- XIV. Roadmap thực hiện (5 milestone)
- XV. Kịch bản Demo
- XVI. Kiểm thử và đánh giá
- XVII. Rủi ro và phương án xử lý
- XVIII. Phân chia công việc — Nhóm 5 sinh viên
- XIX. Kết quả đầu ra
- XX. Hướng phát triển sau này
- XXI. Timeline tổng thể
- XXII. Tiêu chí hoàn thành đề tài

---

## I. Thông tin tổng quan

### 1. Tên đề tài

Hệ thống quản lý phòng trọ phường Ngũ Hành Sơn (Quản lý nhà trọ đa khu trọ).

### 2. Loại dự án

Dự án phần mềm quy mô nhóm (5 sinh viên) phục vụ mục đích học tập môn Lập trình mạng. Dự án xây dựng hệ thống client-server hoàn chỉnh: client .NET 8 WinForms + WebView2, server console .NET 8 lắng nghe TCP, cơ sở dữ liệu MySQL 8.0 — mô phỏng vận hành nhà trọ thực tế với 6 vai trò người dùng.

### 3. Mục tiêu chính

Xây dựng hệ thống quản lý phòng trọ trong đó kiến trúc TCP Client-Server được sử dụng để:

- Tập trung toàn bộ nghiệp vụ và dữ liệu về một server duy nhất; client không kết nối trực tiếp MySQL.
- Thực thi phân quyền 6 vai trò ngay tại server (deny-by-default), client chỉ là giao diện.
- Đảm bảo mọi thao tác đều có requestId tương phản, chống trộn gói khi nhiều request song song.
- Ghi nhận vết mọi biến động cư trú, hóa đơn, sự cố phục vụ tra cứu và xuất báo cáo.
- Minh họa khả năng ứng dụng giao thức TCP tự thiết kế (framing newline-delimited JSON) trong bài toán thực tế.

### 4. Phạm vi

Quy trình xử lý cốt lõi của hệ thống:

Client WinForms/WebView2 → TCP packet JSON → Server xác thực phiên + RBAC → Service nghiệp vụ → Repository → MySQL → Response JSON về đúng requestId

Dự án mô phỏng quy mô phường Ngũ Hành Sơn: đa khu trọ, đa phòng, đa người thuê. Không hướng tới thương mại hóa; tích hợp SePay là mô phỏng đối soát qua webhook, không phải cổng thanh toán thực tế.

---

## II. Vấn đề cần giải quyết

### 1. Thực trạng

Việc quản lý nhà trọ tại phường hiện gặp các vấn đề:

- Chủ trọ sở hữu nhiều khu trọ, theo dõi bằng sổ tay/Excel: hóa đơn thất lạc, công nợ nhầm lẫn.
- Quản lý được phân công nhưng không có công cụ: không biết phòng nào trống, ai chưa đóng tiền, sự cố nào treo.
- Người thuê không tra được cước, không có kênh báo sự cố, phải gặp mặt trực tiếp.
- Lễ tân/nhân viên nhận lịch xem phòng bằng giấy: trùng lịch, thất thoát yêu cầu.
- Công an phường khó tra cứu lưu trú, tạm trú khi làm việc hành chính.
- Nếu xây app giữ dữ liệu ở client hoặc cho client kết nối database trực tiếp: dữ liệu bị sửa trái phép, phân quyền chỉ nằm ở giao diện — vô nghĩa.

### 2. Ví dụ minh họa

Quản lý khu A sửa hóa đơn phòng 101 vì "khách báo nhầm":

- Nếu client kết nối MySQL trực tiếp: sửa xong, không ai biết ai sửa gì, khi nào.
- Với kiến trúc TCP + server: mọi sửa đổi đi qua service có kiểm tra quyền (Quản lý không được xóa hóa đơn đã thu), có trạng thái bất biến `DaThu`, có nhật ký sự cố/đề xuất — dữ liệu chỉ có thể thay đổi theo đúng quy trình.

---

## III. Giải pháp đề xuất

Hệ thống tách thành hai tầng rõ ràng: client chỉ render giao diện, server nắm toàn bộ nghiệp vụ và dữ liệu.

```
┌─────────────────────────────┐
│  CLIENT (WinForms+WebView2)  │
│  HTML/CSS/JS cục bộ          │
└──────────────┬───────────────┘
               │  TCP 8888 — JSON per line
               ▼
┌─────────────────────────────┐
│   SERVER (Console .NET 8)    │
│   TcpServer → RequestRouter  │
│   SessionStore + RBAC        │
│   Services → Repositories    │
└──────────────┬───────────────┘
               ▼
        MySQL 8.0 (InnoDB, utf8mb4)
```

Bảng phân chia lưu trữ:

| Tầng | Nội dung |
|---|---|
| Client (Assets/wwwroot) | HTML/CSS/JS cho 6 shell vai trò + trang public đặt lịch |
| Server (Services) | Toàn bộ nghiệp vụ: hợp đồng, hóa đơn, sự cố, thống kê |
| Server (Repositories) | Mọi truy vấn SQL parameterized, transaction khóa dòng |
| MySQL | 11 bảng dữ liệu nghiệp vụ |

Khi cần kiểm tra quyền, server thực hiện trước service: packet không có phiên hợp lệ hoặc vai trò không được phép → từ chối ngay, không chạm dữ liệu.

---

## IV. Mục tiêu cụ thể (6 phân hệ chức năng)

Sau khi hoàn thành, hệ thống thực hiện được 6 phân hệ chức năng chính theo workbook đặc tả Input/Output — Quyền hạn — Luồng dữ liệu:

### Phân hệ 1: Tài khoản & phân quyền (M1)

- Đăng nhập/đăng xuất/đổi mật khẩu cho 6 vai trò trên một bảng `tai_khoan` hợp nhất.
- Khóa/mở khóa/reset mật khẩu tài khoản (Chủ trọ).
- Ma trận quyền vai trò → hành động, gieo mặc định, thu hồi giữ nguyên sau nâng cấp.
- Cấu hình nhận tiền (STK, ngân hàng, mẫu QR) — API token chỉ Chủ trọ thấy.

### Phân hệ 2: Khu trọ — Phòng — Người thuê (M2)

- CRUD khu trọ, phân công quản lý theo khu (mỗi quản lý tối đa 1 khu).
- CRUD phòng: số phòng unique trong khu, giá thuê, sức chứa, trạng thái (Trong/DaThue/BaoTri).
- Hồ sơ khách thuê 1-1 với tài khoản: CCCD 12 số unique, SĐT, quê quán.
- Đăng ký tài khoản public, chuyển phòng, trả phòng, tìm kiếm theo tên/CCCD/SĐT (tối đa 50 kết quả).
- Quản lý chỉ thao tác trong khu được gán — server cưỡng chế bằng session.

### Phân hệ 3: Hợp đồng — QR/PIN nhận phòng — Phụ lục (M3)

- Lập hợp đồng trực tiếp hoặc hợp đồng chờ QR: sinh mã QR (token 128-bit) + PIN 8 số, lưu hash, trả dạng rõ đúng một lần.
- Nhận phòng bằng quét QR webcam / chọn ảnh QR / nhập PIN — nguyên tử, chống replay (hash xóa sạch sau kích hoạt).
- Tái phát hành mã làm mã cũ vô hiệu ngay.
- Gia hạn hợp đồng; chấm dứt của Chủ trọ bắt buộc lý do + bàn giao dự kiến ≥ hôm nay + 30 ngày (Luật Nhà ở 2023).
- Phụ lục người ở chung: phòng 3 người = 1 đại diện + 2 dòng phụ lục, không vượt sức chứa; bản in có khung chữ ký.

### Phân hệ 4: Điện nước — Hóa đơn — Đề xuất — Sự cố (M4)

- Ghi chỉ số điện nước theo kỳ YYYY-MM, tự điền số cũ kỳ liền trước, tính tiền theo Khối hoặc Theo người.
- Lập hóa đơn tháng (tiền phòng + điện + nước + phí khác), xác nhận thu tiền thủ công hoặc đối soát SePay tự động, hóa đơn `DaThu` bất biến.
- Đề xuất chờ Chủ trọ duyệt cho thao tác nhạy cảm của Quản lý: xóa phòng, chấm dứt hợp đồng, sửa giá phòng, xóa hóa đơn chưa thu, xử lý sự cố mức cao.
- Sự cố 4 mức độ (Thap/TrungBinh/Cao/KhanCap) với máy trạng thái 8 trạng thái, nhật ký tiến độ, đính kèm ảnh ≤ 2MB kiểm tra magic bytes.

### Phân hệ 5: Lịch kiểm tra — Thống kê — Lưu trú (M5)

- Trang public xem phòng trống + đặt lịch xem phòng không cần đăng nhập; lễ tân duyệt/từ chối/gán nhân viên.
- Nhân viên nhận việc, nhập kết quả, hoàn thành theo chuyên môn (DonPhong/ThuTien/KyThuat).
- Thống kê vận hành (tỷ lệ lấp đầy, phòng nợ, sự cố, lịch) không chứa bất kỳ con số tiền nào cho Quản lý.
- Thống kê doanh thu độc quyền Chủ trọ: theo khu, xu hướng tháng/quý/năm, hiệu suất quản lý, dự báo kỳ tới.
- Tra cứu lưu trú cho Công an: theo CCCD/tên/SĐT/phòng, danh sách tạm trú, chưa đăng ký, xuất file CSV/Excel an toàn.

### Phân hệ 6: Giao diện 6 shell (M1–M5)

- Mỗi vai trò một shell riêng với userbar (tên/vai trò/chuyên môn/khu), trạng thái kết nối, đăng xuất.
- Trang public đặt lịch; shell khách thuê có quét QR webcam (quyền camera chỉ cấp cho URL khachthue/).

---

## V. Đối tượng sử dụng

Hệ thống phục vụ 6 vai trò người dùng (mỗi người một tài khoản, một vai trò):

### 1. Chủ trọ (ChuTro)

- Quản trị toàn hệ thống: tài khoản, quyền vai trò, cấu hình nhận tiền.
- CRUD khu trọ, phòng; duyệt/từ chối mọi đề xuất; chấm dứt hợp đồng trực tiếp.
- Xem thống kê doanh thu toàn hệ thống — vai trò duy nhất thấy số tiền.

### 2. Quản lý (QuanLy)

- Được gán đúng một khu trọ: quản lý phòng, người thuê, điện nước, hóa đơn, thu tiền trong khu.
- Gửi đề xuất cho thao tác nhạy cảm; xử lý sự cố mức thấp/trung bình.
- Xem thống kê vận hành KHÔNG có trường doanh thu.

### 3. Lễ tân (LeTan)

- Duyệt/từ chối lịch xem phòng, gán nhân viên theo chuyên môn, tra cứu phòng để tư vấn.

### 4. Nhân viên (NhanVien)

- Xem việc được gán, nhận việc, nhập kết quả, hoàn thành. Chuyên môn: dọn phòng / thu tiền / kỹ thuật.

### 5. Khách thuê (KhachThue)

- Đăng ký tài khoản public, nhận phòng bằng QR/PIN, xem hợp đồng/người ở chung của mình.
- Xem chỉ số điện nước, hóa đơn, lịch sử thanh toán, QR chuyển khoản của phòng mình.
- Báo sự cố, theo dõi tiến độ, xác nhận đóng sự cố.

### 6. Công an (CongAn)

- Chỉ đọc: tra cứu công dân theo CCCD/tên/SĐT/phòng, danh sách tạm trú, chưa đăng ký tạm trú.
- Xuất báo cáo lưu trú theo phường (CSV/Excel). Mọi thao tác ghi bị server từ chối.

Ngoài ra: khách vãng lai (public) xem phòng trống và đặt lịch xem phòng không cần tài khoản.

---

## VI. Công nghệ sử dụng (TCP Client-Server)

### .NET 8

Nền tảng chính cho cả client và server, nhờ runtime ổn định, WinForms + WebView2 tích hợp sẵn và tài liệu phong phú.

### WinForms + WebView2

Client WinForms làm host, WebView2 render toàn bộ giao diện HTML/CSS/JS cục bộ qua asset host `https://app.quanlytro.local` — kết hợp dễ phát triển web với ứng dụng desktop thuần.

### TCP Socket + JSON

Giao thức tự thiết kế trên `TcpListener/TcpClient`: mỗi request/response là một dòng JSON UTF-8 kết thúc ký tự xuống dòng, có requestId tương phản — minh họa trực tiếp nội dung môn học: framing, đa luồng, đồng bộ ghi socket.

### MySQL 8.0

Lưu trữ 11 bảng nghiệp vụ, InnoDB transaction, khóa dòng `FOR UPDATE` chống đua dữ liệu.

### System.Security.Cryptography

PBKDF2-SHA256 băm mật khẩu (600k vòng); CSPRNG sinh mã QR/PIN; SHA-256 băm mã nhận phòng; so sánh thời gian hằng chống timing attack.

---

## VII. Công nghệ phần mềm

| Thành phần | Công nghệ |
|---|---|
| Client desktop | .NET 8 WinForms |
| Giao diện client | WebView2 1.0.2903.40 + HTML/CSS/JS cục bộ |
| Server | .NET 8 Console (TCP listener cổng 8888) |
| Giao thức | TCP newline-delimited JSON (RequestPacket/ResponsePacket) |
| CSDL | MySQL 8.0, InnoDB, utf8mb4 |
| Driver CSDL | MySqlConnector 2.4.0 |
| Bảo mật mật khẩu | PBKDF2-SHA256, 600.000 vòng + salt |
| Mã QR/PIN | CSPRNG 128-bit token + PIN 8 số, lưu SHA-256 hash |
| Quét QR client | qrcode.min.js + jsqr.min.js (local, không CDN) |
| Xuất Excel (M5) | ClosedXML 0.104.2 |
| Unit/Integration test | MSTest 3.7.0 + Test.Sdk 17.12.0 |
| Quản lý mã nguồn | Git / GitHub |
| Môi trường phát triển | VS Code / Visual Studio 2022 |

### Vì sao chọn công nghệ này?

- WinForms + WebView2: dự án cũ đã chạy được, kế thừa kinh nghiệm; giao diện web dễ làm đẹp hơn WinForms thuần mà không cần Electron.
- TCP JSON tự thiết kế: đúng chủ đề môn Lập trình mạng — tự làm framing, requestId correlation, đa client bất đồng bộ thay vì dùng gRPC/HTTP có sẵn.
- MySQL + InnoDB: transaction, khóa dòng, unique constraint — đủ công cụ thực thi các bất biến nghiệp vụ (một hợp đồng mở mỗi phòng, hóa đơn bất biến).
- PBKDF2 thay vì SHA-256 trực tiếp: chuẩn OWASP cho lưu mật khẩu.
- MSTest: tích hợp sẵn Visual Studio, không cần thêm framework.

---

## VIII. Kiến trúc hệ thống

Kiến trúc tổng thể gồm 3 lớp: giao diện WebView2, tầng nghiệp vụ server, tầng lưu trữ MySQL.

```
┌──────────────────────────────────────────────┐
│                 USER (6 vai trò)              │
└───────────────────────┬───────────────────────┘
                        ▼
┌──────────────────────────────────────────────┐
│        CLIENT — WinForms + WebView2           │
│  Assets/wwwroot: login + 6 shell + public     │
│  Bridge: message requestId → packet TCP       │
│  RoleRouter: shell theo vai trò               │
└───────────────────────┬───────────────────────┘
                        │ TCP 8888
                        │ RequestPacket{requestId,action,token,data}
                        ▼
┌──────────────────────────────────────────────┐
│              SERVER — Console .NET 8          │
│  TcpServer: đa client bất đồng bộ,            │
│    framing theo dòng, giới hạn 256KB→3MB,     │
│    SemaphoreSlim đồng bộ ghi                  │
│  RequestRouter: AuthPolicy + AllowedRoles     │
│  SessionStore: phiên, lockout 5 sai→1 phút    │
│  Services (nghiệp vụ) → Repositories (SQL)    │
└───────────────────────┬───────────────────────┘
                        ▼
┌──────────────────────────────────────────────┐
│        MySQL 8.0 — 11 bảng, InnoDB            │
│   Transaction + FOR UPDATE + UNIQUE guard     │
└──────────────────────────────────────────────┘
```

Nguyên tắc kiến trúc:

- Client KHÔNG bao giờ kết nối MySQL — không connection string phía client.
- RBAC kiểm tra server-side trước service; menu client chỉ là UX.
- Mọi packet bắt buộc `requestId`; server sao chép nguyên requestId vào response.
- Deny-by-default: action không khai báo AllowedRoles thì không ai gọi được.

---

## IX. Thiết kế dữ liệu (11 bảng)

Toàn bộ bảng đặt tên snake_case không dấu. Tiền dùng DECIMAL, không dùng floating point.

### 1. Bảng tai_khoan

| Field | Ý nghĩa |
|---|---|
| id | Khóa chính |
| ten_dang_nhap | Tên đăng nhập — unique |
| mat_khau_hash | PBKDF2 hash + salt |
| ho_ten | Họ tên |
| vai_tro | ChuTro/QuanLy/LeTan/NhanVien/KhachThue/CongAn |
| khu_tro_id | Phạm vi khu — chỉ QuanLy có giá trị |
| chuyen_mon | DonPhong/ThuTien/KyThuat — chỉ NhanVien |
| bi_khoa / ly_do_khoa | Khóa tài khoản |
| ngay_tao / ngay_cap_nhat | Timestamps |

### 2. Bảng quyen_vai_tro (+ quyen_mac_dinh_da_ap_dung)

| Field | Ý nghĩa |
|---|---|
| vai_tro | Vai trò |
| hanh_dong | Tên action (case-sensitive) |

Khóa chính (vai_tro, hanh_dong). Bảng `quyen_mac_dinh_da_ap_dung` ghi vết quyền mặc định đã gieo để nâng cấp không khôi phục quyền đã thu hồi.

### 3. Bảng cau_hinh_he_thong

Cấu hình nhận tiền: số tài khoản, ngân hàng, chủ TK, mẫu QR, API token SePay (chỉ trả cho ChuTro).

### 4. Bảng khu_tro

| Field | Ý nghĩa |
|---|---|
| id | Khóa chính |
| ten_khu_tro / dia_chi | Tên, địa chỉ khu |
| quan_ly_tai_khoan_id | Quản lý được gán — một quản lý tối đa 1 khu |
| bi_ngung_hoat_dong | Ngừng hoạt động thay vì xóa khi còn phòng |

### 5. Bảng phong

| Field | Ý nghĩa |
|---|---|
| id | Khóa chính |
| khu_tro_id | FK khu trọ |
| so_phong | Số phòng — UNIQUE(khu_tro_id, so_phong) |
| gia_thue | DECIMAL > 0 |
| so_nguoi_toi_da | Sức chứa > 0 |
| trang_thai | Trong / DaThue / BaoTri |

### 6. Bảng ho_so_khach_thue

| Field | Ý nghĩa |
|---|---|
| tai_khoan_id | PK + FK 1-1 với tai_khoan (không có mật khẩu ở đây) |
| cccd | CHAR(12) unique toàn hệ thống |
| ngay_sinh / so_dien_thoai / que_quan / noi_lam_viec | Thông tin cá nhân |
| phong_id | Phòng đang ở — nullable |
| da_dang_ky_tam_tru | Cờ tạm trú |

### 7. Bảng lich_su_cu_tru

| Field | Ý nghĩa |
|---|---|
| id | Khóa chính |
| tai_khoan_khach_id / phong_id / khu_tro_id | Đối tượng biến động |
| loai_bien_dong | NhanPhong / ChuyenDen / ChuyenDi / TraPhong |
| nguoi_thuc_hien_id / thoi_diem | Vết thực hiện |

M2 ghi biến động nguyên tử trong transaction; M3 ghi NhanPhong khi kích hoạt QR; M5 đọc để xuất báo cáo.

### 8. Bảng hop_dong + phu_luc_hop_dong

| Field (hop_dong) | Ý nghĩa |
|---|---|
| id / phong_id | Khóa chính, FK phòng |
| nguoi_dai_dien_tai_khoan_id | Người đại diện hợp đồng |
| ngay_bat_dau / ngay_ket_thuc | CHECK ket_thuc > bat_dau |
| gia_thue / tien_coc | DECIMAL, tien_coc ≥ 0 |
| trang_thai | ChoNhanPhong / HieuLuc / HetHan / ChamDut |
| ma_qr_token_hash / ma_pin_hash | SHA-256 hash, xóa sạch khi kích hoạt |
| ma_het_han_luc / kich_hoat_luc | Hạn mã, thời điểm kích hoạt |

`phu_luc_hop_dong`: hop_dong_id, ho_ten, cccd, ngay_sinh, que_quan, so_dien_thoai — UNIQUE(hop_dong_id, cccd), ON DELETE CASCADE.

Ràng buộc: mỗi phòng tối đa 1 hợp đồng đang mở (ChoNhanPhong hoặc HieuLuc), kiểm tra trong transaction khóa dòng phong FOR UPDATE.

### 9. Bảng chi_so_dien_nuoc

| Field | Ý nghĩa |
|---|---|
| id / phong_id / ky_cuoc | UNIQUE(phong_id, ky_cuoc) — ky_cuoc CHAR(7) YYYY-MM |
| dien_cu / dien_moi / gia_dien | dien_moi ≥ dien_cu |
| hinh_thuc_nuoc | Khoi (nuoc_moi ≥ nuoc_cu) hoặc Nguoi (so_nguoi_nuoc ≥ 0) |
| tien_dien / tien_nuoc | Server tính, decimal 2 chữ số |
| nguoi_ghi_id / ngay_ghi | Vết ghi chỉ số |

Chỉ được sửa khi chưa có hóa đơn của cùng (phong_id, ky_cuoc).

### 10. Bảng hoa_don + thanh_toan + giao_dich_sepay

| Field (hoa_don) | Ý nghĩa |
|---|---|
| id / phong_id / hop_dong_id / ky_cuoc | UNIQUE(phong_id, ky_cuoc) |
| tien_phong / tien_dien / tien_nuoc / phi_khac / tong_tien | DECIMAL |
| trang_thai | ChuaThu / DaThu — DaThu BẤT BIẾN (guard WHERE trang_thai='ChuaThu') |
| ngay_dong | Ngày đóng tiền |

`thanh_toan`: hoa_don_id, so_tien, phuong_thuc (TienMat/ChuyenKhoan/SePay), ma_tham_chieu unique khi SePay.
`giao_dich_sepay`: sepay_id unique (idempotent chống khớp trùng), noi_dung, so_tien, da_khop.

### 11. Bảng de_xuat + su_co + nhat_ky_su_co + dat_lich_kiem_tra

`de_xuat`: khu_tro_id, loai_de_xuat (XoaPhong/ChamDutHopDong/SuaGiaPhong/XoaHoaDon/XuLySuCo), doi_tuong_id, du_lieu_kem_theo_json, ly_do, trang_thai (ChoDuyet/DaDuyet/TuChoi) — không cho 2 đề xuất ChoDuyet trùng (loai, doi_tuong).

`su_co`: ma_su_co unique (SC-202611-0001), khu_tro_id, phong_id, nguoi_bao_id, loai_su_co, tieu_de, mo_ta, muc_do (Thap/TrungBinh/Cao/KhanCap), trang_thai (Moi/DaTiepNhan/ChoChuTroDuyet/DaDuyetDangXuLy/BiTuChoi/DaXuLyXong/DaDong).

`nhat_ky_su_co`: su_co_id, nguoi_ghi_id, hanh_dong, noi_dung, tep_anh_duong_dan (≤2MB, magic bytes PNG/JPEG/WebP, tên ngẫu nhiên chống path traversal).

`dat_lich_kiem_tra`: phong_id, ho_ten, so_dien_thoai, thoi_gian, trang_thai (ChoDuyet/DaDuyet/TuChoi/DangLam/HoanThanh), tai_khoan_nhan_vien_id (ON DELETE SET NULL), ket_qua, ly_do_tu_choi.

---

## X. Thiết kế giao thức TCP & phân quyền RBAC

### 1. Packet giao thức

```json
RequestPacket:  { "requestId": "...", "action": "DANG_NHAP", "token": null, "data": {} }
ResponsePacket: { "requestId": "...", "success": true, "message": "", "data": {} }
```

Mỗi packet là một dòng JSON UTF-8 kết thúc ký tự xuống dòng. Server dùng ConcurrentDictionary<requestId, TaskCompletionSource> để tương phản response với request; ghi socket đồng bộ bằng SemaphoreSlim để nhiều request song song không trộn gói. Giới hạn frame mặc định 262144 bytes, cấu hình tới 3145728 cho upload ảnh M4. Packet quá lớn hoặc lỗi parse → trả lỗi giao thức rồi đóng kết nối, không làm sập server.

### 2. RBAC deny-by-default

- Mỗi action khai báo AllowedRoles (hoặc Public) trong registry; test CI fail khi action thiếu handler hoặc permission metadata.
- Server kiểm tra phiên + vai trò TRƯỚC khi gọi service.
- Scope khu: QuanLy chỉ thấy dữ liệu khu_tro_id trong session — không nhận khuTroId từ payload; truy vấn ngoài khu trả lỗi không tiết lộ bản ghi có tồn tại.
- Chủ trọ bypass RBAC quản trị nhưng không bypass tính hợp lệ phiên/khóa tài khoản; không tự khóa chính mình.
- CongAn: server từ chối mọi action ghi dù client gọi trực tiếp qua packet giả.

### 3. Bảo mật

- PBKDF2-SHA256 600.000 vòng + salt riêng từng tài khoản.
- Sai đăng nhập: thông báo chung; 5 lần sai liên tiếp → khóa tạm 1 phút (TimeProvider injectable để test).
- Mã QR/PIN: lưu SHA-256 hash + hạn dùng; so sánh thời gian hằng; xóa sạch sau kích hoạt (chống replay); tái phát hành vô hiệu mã cũ.
- Ảnh đính kèm: ≤ 2MB, magic bytes PNG/JPEG/WebP, đổi tên ngẫu nhiên.
- Không trả mat_khau_hash, api_token hay stack trace trong DTO. Không log mật khẩu/hash/token.

---

## XI. Quy trình nghiệp vụ chính

### Quy trình 1: Lập hợp đồng chờ QR + nhận phòng

```
Bắt đầu
   ↓
Chủ trọ/Quản lý lập hợp đồng (phòng, đại diện, giá, thời hạn)
   ↓
Server khóa dòng phòng FOR UPDATE — kiểm tra không có hợp đồng mở
   ↓
Sinh QR token 128-bit + PIN 8 số (CSPRNG) → lưu hash → trả dạng rõ 1 lần
   ↓
In phiếu QR cho khách
   ↓
Khách thuê quét QR / chọn ảnh / nhập PIN
   ↓
Server trong 1 transaction: khóa hợp đồng + phòng, khớp hash thời gian hằng,
kiểm tra hạn + sức chứa → gán phòng, hợp đồng sang HieuLuc, xóa hash, phòng DaThue
   ↓
Ghi lich_su_cu_tru (NhanPhong)
   ↓
Kết thúc
```

### Quy trình 2: Ghi điện nước → hóa đơn → thu tiền

```
Ghi chỉ số (tự điền số cũ kỳ trước) → Server tính tiền → Lập hóa đơn tháng
   ↓
Khách xem hóa đơn của mình → chuyển khoản QR / SePay tự khớp / Quản lý xác nhận tiền mặt
   ↓
Hóa đơn sang DaThu — BẤT BIẾN (mọi sửa/xóa bị chặn kể cả qua đề xuất)
```

### Quy trình 3: Đề xuất chờ duyệt (thao tác nhạy cảm của Quản lý)

```
Quản lý tạo đề xuất (xóa phòng / chấm dứt HĐ / sửa giá / xóa hóa đơn / xử lý sự cố cao)
   ↓
Chủ trọ bấm Duyet → Server 1 transaction: khóa dòng de_xuat,
gọi đúng hàm nghiệp vụ gốc (TerminateByOwnerAsync...), de_xuat sang DaDuyet
   ↓
Điều kiện nghiệp vụ không còn đúng → rollback + trả lỗi rõ ràng
```

---

## XII. Quy trình nhận phòng QR/PIN (chống replay)

```
Bắt đầu
   ↓
Khách nhập/quét mã
   ↓
Rate-limit: 5 lần sai → khóa nhận phòng 1 phút
   ↓
Lấy hợp đồng ChoNhanPhong của session.TaiKhoanId (FOR UPDATE)
   ↓
Mã hết hạn? ─── Có → Từ chối
   ↓ Không
SHA-256(mã nhập) khớp ma_qr_token_hash hoặc ma_pin_hash (so sánh thời gian hằng)?
   ↓ Không khớp → Từ chối + đếm lần sai
   ↓ Khớp
Phòng BaoTri? Đủ sức chứa? ─── Không đạt → Từ chối
   ↓ Đạt
1 transaction: gán phòng, hợp đồng HieuLuc, ghi kich_hoat_luc,
XÓA ma_qr_token_hash + ma_pin_hash + ma_het_han_luc, phòng DaThue
   ↓
Quét lại cùng mã → Không tìm thấy hợp đồng → CHỐNG REPLAY
   ↓
Kết thúc
```

---

## XIII. Thiết kế giao diện

Giao diện web trong WebView2, tập trung tính rõ ràng và dễ demo. Client có trang đăng nhập chung + 6 shell theo vai trò + 1 trang public.

### Shell Chủ trọ

```
===============================================
  QUẢN LÝ NHÀ TRỘ — [ChuTro] Nguyen Van A
===============================================
  Khu trọ      | Phòng    | Hợp đồng  | Điện nước
  Hóa đơn      | Đề xuất duyệt | Sự cố
  Tài khoản & phân quyền | Cấu hình nhận tiền
  Thống kê DOANH THU (độc quyền)
===============================================
```

### Shell Quản lý

```
===============================================
  KHU A — [QuanLy] — Trạng thái: Đã kết nối
===============================================
  Phòng khu tôi | Người thuê | Điện nước
  Hóa đơn & thu tiền | Đề xuất đã gửi | Sự cố
  Thống kê VẬN HÀNH (không doanh thu)
===============================================
```

### Shell Lễ tân / Nhân viên / Công an / Khách thuê

- Lễ tân: lịch chờ duyệt lên đầu, duyệt/từ chối (lý do bắt buộc), gán nhân viên lọc theo chuyên môn, tra cứu phòng.
- Nhân viên: danh sách việc của tôi → Nhận việc → Nhập kết quả → Hoàn thành.
- Công an: ô tra cứu CCCD/tên/SĐT/phòng, tab tạm trú, chưa đăng ký, thống kê, nút xuất file — KHÔNG nút ghi.
- Khách thuê: hợp đồng + người ở chung của phòng mình, hóa đơn + QR chuyển khoản, báo sự cố; trang nhận phòng quét QR webcam / chọn ảnh / nhập PIN 8 số.
- Public (datlich.html): danh sách phòng trống (số phòng + giá), form đặt lịch xem phòng.

Giao diện tối thiểu: keyboard accessible, label đầy đủ cho input, focus/error state rõ.

---

## XIV. Roadmap thực hiện (5 milestone)

Với quy mô 5 sinh viên làm việc song song theo chuyên môn, dự án chia 5 milestone độc lập có thứ tự. Mỗi milestone là một sprint: mở sprint mới khi sprint trước `dotnet test` xanh và chạy được end-to-end.

### Milestone 1: Nền tảng — TCP — Tài khoản — RBAC

- Solution .NET 8 (client WinForms+WebView2, server console, shared, tests).
- Giao thức TCP newline-delimited JSON + requestId correlation.
- Schema nền (tai_khoan, quyen_vai_tro, cau_hinh_he_thong), PBKDF2, lockout, session.
- RBAC router deny-by-default + PermissionSeeder.
- WebView2 host + trang login + 6 shell empty state.

**Deliverable:** đăng nhập đúng 6 vai trò, mọi packet có requestId, router từ chối sai quyền.

### Milestone 2: Khu trọ — Phòng — Người thuê

- Schema khu_tro, phong, ho_so_khach_thue, lich_su_cu_tru.
- CRUD khu/phòng/người thuê, phân công quản lý, scope khu server-side.
- Đăng ký public, chuyển phòng, trả phòng, tìm kiếm (tối đa 50 kết quả).

**Deliverable:** quản lý khu A không chạm được dữ liệu khu B dù gửi packet giả.

### Milestone 3: Hợp đồng — QR/PIN — Phụ lục

- Schema hop_dong, phu_luc_hop_dong.
- Lập/gia hạn/chấm dứt hợp đồng (luật 30 ngày), CheckinCodeGenerator CSPRNG + hash.
- Nhận phòng QR/PIN nguyên tử chống replay, tái phát hành, rate-limit dò PIN.
- Phụ lục quy tắc 3 người, bản in escape XSS, quyền camera chỉ cho URL khachthue/.

**Deliverable:** đua tạo hợp đồng cùng phòng → đúng 1 thành công; replay QR → đúng 1 kích hoạt.

### Milestone 4: Điện nước — Hóa đơn — Đề xuất — Sự cố

- 7 bảng M4, bất biến DaThu, đối soát SePay idempotent.
- Đề xuất duyệt nguyên tử gọi hàm nghiệp vụ gốc trong cùng transaction.
- Sự cố 4 mức với máy trạng thái 8 trạng thái + nhật ký + ảnh đính kèm an toàn.

**Deliverable:** hóa đơn đã thu không thể xóa bằng cách nào; đua thanh toán vs duyệt xóa an toàn.

### Milestone 5: Lịch kiểm tra — Thống kê — Lưu trú

- dat_lich_kiem_tra + máy trạng thái 5 trạng thái, chống spam đặt lịch.
- Trang public datlich.html, shell lễ tân/nhân viên hoàn chỉnh.
- Thống kê vận hành (không tiền) vs doanh thu (chỉ ChuTro); xuất CSV/Excel công an.

**Deliverable:** 100% scope workbook Excel 6 vai trò; hồi quy tổng M1–M5 xanh.

---

## XV. Kịch bản Demo

Đây là phần quan trọng khi báo cáo trước giảng viên — minh họa trọn vòng đời và điểm mạnh kiến trúc TCP + RBAC:

1. Mở client → đăng nhập Chủ trọ → tạo khu trọ A + phòng 101 (sức chứa 3, giá 2.5 triệu).
2. Tạo tài khoản Quản lý, phân công khu A → đăng nhập lại bằng Quản lý → chỉ thấy khu A.
3. Đăng ký khách thuê public (CCCD 012345678901) → lập hợp đồng chờ QR cho phòng 101 → server trả QR + PIN một lần → in phiếu.
4. Đăng nhập khách thuê → quét QR webcam nhận phòng → hợp đồng HieuLuc, phòng sang DaThue, lịch sử cư trú ghi NhanPhong.
5. Quét lại cùng QR → server từ chối — minh họa CHỐNG REPLAY (mã đã xóa khỏi database).
6. Quản lý ghi chỉ số điện nước kỳ hiện tại (số cũ tự điền) → lập hóa đơn → khách thấy hóa đơn của mình + QR chuyển khoản.
7. Quản lý xác nhận thu tiền → hóa đơn DaThu → thử xóa hóa đơn bằng packet giả → server từ chối — minh họa BẤT BIẾN + RBAC server-side.
8. Quản lý gửi đề xuất chấm dứt hợp đồng (lý do + ngày bàn giao) → Chủ trọ duyệt → hợp đồng ChamDut.
9. Khách báo sự cố nước mức Cao → Quản lý tiếp nhận → đề xuất xử lý → Chủ trọ duyệt → nhật ký tiến độ.
10. Khách vãng lai đặt lịch xem phòng public → Lễ tân duyệt + gán nhân viên dọn phòng → nhân viên nhận việc, hoàn thành.
11. Thống kê: Quản lý xem vận hành (chỉ số không tiền) → Chủ trọ xem doanh thu theo khu.
12. Công an tra cứu CCCD → danh sách tạm trú → xuất file Excel.

Bước 5 và 7 là cao trào demo: chứng minh phân quyền và bảo vệ dữ liệu nằm ở server, không phải giao diện.

---

## XVI. Kiểm thử và đánh giá

| Tiêu chí | Mục tiêu |
|---|---|
| Đăng nhập 6 vai trò | Thành công, lockout đúng 5 sai → 1 phút |
| RBAC deny-by-default | Mọi action sai quyền bị từ chối ở server |
| Scope khu Quản lý | Không đọc/sửa khu khác dù packet giả |
| Đua tạo hợp đồng cùng phòng | Đúng 1 thành công |
| Replay QR/PIN | Đúng 1 kích hoạt, mã wiped |
| Bất biến hóa đơn DaThu | Không thể sửa/xóa bằng bất kỳ đường nào |
| Đối soát SePay trùng | Idempotent, không nhân bản thanh toán |
| Máy trạng thái sự cố/lịch | Chuyển sai bước bị chặn |
| Thống kê vận hành QuanLy | Không chứa trường tiền (quét DTO + file xuất) |
| Xuất file công an | Chỉ trong thư mục cho phép, không path traversal |
| TCP framing | Fragmented, quá giới hạn, malformed JSON, disconnect — server không sập |

Test tích hợp chỉ chạy trên database `quanly_nhatro_test` — fail nếu connection string trỏ DB thật. Baseline mục tiêu: toàn bộ test MSTest xanh trước mỗi merge.

---

## XVII. Rủi ro và phương án xử lý

| Rủi ro | Giải pháp |
|---|---|
| Đua dữ liệu (2 request cùng lúc) | Transaction + SELECT ... FOR UPDATE + UNIQUE constraint; test đồng thời bắt buộc cho mỗi điểm đua |
| Trộn gói TCP khi đa client | requestId correlation + SemaphoreSlim đồng bộ ghi; test fragmented/multi-packet |
| WebView2 asset/loading lỗi | Asset host cục bộ chuẩn hóa; fallback nhập PIN khi camera/QR lỗi |
| Quyền lọt qua payload giả mạo | RBAC + scope đọc từ session, không tin payload; test packet giả cho từng vai trò |
| SePay webhook sai/trùng | sepay_id unique + idempotent; môi trường demo dùng FakeSePayClient |
| Ảnh đính kèm nguy hiểm | Magic bytes + giới hạn 2MB + tên ngẫu nhiên, không dùng tên gốc client |
| Leak mật khẩu/token vào log | Không log secret; DTO không chứa hash; review bắt buộc |
| Tiến độ 5 thành viên lệch nhau | 1 milestone = 1 sprint; PR review theo chuyên môn; SV1 quyết xung đột interface |
| Milestone sau phụ thuộc milestone trước | Ràng buộc hợp đồng Consumes/Produces ghi rõ trong từng Spec; interface đổi phải cập nhật test milestone phụ thuộc cùng PR |

Rủi ro lớn nhất: **đua dữ liệu và lọt quyền qua packet TCP thủ công**. Vì vậy mọi milestone đều có test đồng thời + test giả mạo vai trò trước khi kết thúc sprint, và hệ thống luôn có thể demo bằng FakeSePayClient ngay cả khi tích hợp SePay thật gặp sự cố.

---

## XVIII. Phân chia công việc — Nhóm 5 sinh viên

Đội hình 5 sinh viên chia theo **chuyên môn xuyên suốt cả 5 milestone**: mỗi người giữ một vai trò từ đầu đến cuối, mỗi milestone là một sprint mà cả 5 người cùng tham gia đúng phần chuyên môn của mình.

### Sơ đồ tổ chức nhóm

```
                SV1 — TRƯỞNG NHÓM (TCP/Protocol/RBAC)
                                │
      ┌─────────────┬───────────┼───────────────┬─────────────┐
      ▼             ▼           ▼               ▼             ▼
   SV2 CSDL      SV3 Backend   SV4 Frontend      SV5 QA
  Schema+Repo   Services+DTO   WebView2 UI      Tests
```

### 1. SV1 — Trưởng nhóm: Hạ tầng mạng & Giao thức

| STT | Vai trò | Nhiệm vụ chi tiết |
|---|---|---|
| SV1 | Trưởng nhóm — TCP/Protocol/RBAC | Solution skeleton, giao thức packet RequestPacket/ResponsePacket + requestId correlation, TcpServer đa client, SessionStore + lockout, PermissionService + RequestRouter RBAC deny-by-default, CheckinCodeGenerator CSPRNG (M3), AttachmentStore magic bytes (M4), public exemption + router deny doanh thu (M5); quyết cuối mọi xung đột hợp đồng interface (ActionNames, DTO, schema) |

### 2. SV2 — Kỹ sư CSDL

| STT | Vai trò | Nhiệm vụ chi tiết |
|---|---|---|
| SV2 | Kỹ sư CSDL — Schema & Repository | Toàn bộ schema.sql 11 bảng (unique, FK cascade/SET NULL, CHECK, DECIMAL), SchemaInitializer idempotent, Database connection + transaction helper, TOÀN BỘ Repository (Contract, Appendix, Utility, Invoice, SePay, Proposal, Incident, LichKiemTra, Residence...), guard SQL WHERE trang_thai, ClosedXML xuất file M5 |

### 3. SV3 — Kỹ sư Backend

| STT | Vai trò | Nhiệm vụ chi tiết |
|---|---|---|
| SV3 | Kỹ sư Backend — Nghiệp vụ & Services | Toàn bộ service nghiệp vụ: AuthenticationService (cùng SV1), SiteService/RoomService/TenantService (M2), ContractService (tạo/gia hạn/chấm dứt luật 30 ngày, 2 overload TerminateByOwnerAsync) + AppendixService quy tắc 3 người (M3), UtilityService/InvoiceService bất biến DaThu/SePayReconciliationService idempotent/ProposalService duyệt nguyên tử/IncidentService máy trạng thái 4 mức (M4), LichKiemTraService/OperationalReportService không tiền/RevenueReportService chỉ ChuTro/ExportService (M5), toàn bộ DTO + enum Shared/Models |

### 4. SV4 — Kỹ sư Frontend

| STT | Vai trò | Nhiệm vụ chi tiết |
|---|---|---|
| SV4 | Kỹ sư Frontend — WebView2 UI | Form1 host WebView2 + asset host local, bridge message requestId, 6 shell vai trò + trang login, shell khu/phòng/người thuê (M2), shell hợp đồng + modal QR/PIN in iframe + nhận phòng quét webcam/chọn ảnh/PIN + bản in phụ lục escape XSS (M3), tab điện nước/hóa đơn/đề xuất/sự cố (M4), datlich.html public + shell lễ tân/nhân viên/công an (M5), toàn bộ HTML/CSS/JS Assets/wwwroot |

### 5. SV5 — Kiểm thử & QA

| STT | Vai trò | Nhiệm vụ chi tiết |
|---|---|---|
| SV5 | Kiểm thử & QA — Test tích hợp/bảo mật | TestDatabase + helper chỉ dùng quanly_nhatro_test, toàn bộ test: schema/cascade/unique (M1–M2), đua tạo hợp đồng + replay QR + rate-limit PIN + XSS bản in (M3), bất biến DaThu + idempotent SePay + giả mạo phongId + máy trạng thái (M4), chống spam đặt lịch + quét trường tiền + path traversal xuất file + hồi quy tổng M1–M5 (M5); cổng chặn dotnet test xanh trước mọi merge |

### Ma trận task → người phụ trách theo milestone

| Sprint | SV1 (TCP/Protocol) | SV2 (CSDL) | SV3 (Nghiệp vụ) | SV4 (Frontend) | SV5 (QA) |
|---|---|---|---|---|---|
| **M1** Nền tảng | Solution, packet TCP, SessionStore/lockout, PermissionService/Router | Database, schema nền, PermissionSeeder | AuthenticationService, Login DTO | Form1, bridge, 6 shell | TestDatabase, toàn bộ test M1 |
| **M2** Khu–Phòng–Người thuê | Đăng ký action vào Router | Schema + 4 bảng, repository | Site/Room/Tenant service | Shell Chủ trọ/Quản lý/Khách/Công an | Test scope + đồng thời sức chứa |
| **M3** Hợp đồng–QR/PIN | CheckinCodeGenerator, chống replay | Schema hop_dong/phu_luc, repository | Contract/Appendix service | Shell hợp đồng, nhận phòng, bản in | Test replay + cascade + XSS |
| **M4** Điện nước–Hóa đơn–Đề xuất–Sự cố | AttachmentStore (bảo mật ảnh) | 7 bảng M4, repository | Utility/Invoice/SePay/Proposal/Incident service | Shell thu cước, gửi sự cố | Test bất biến + idempotent + máy trạng thái |
| **M5** Lịch–Thống kê–Lưu trú | Public exemption, router deny doanh thu | Schema dat_lich, repository | LichKiemTra/Report/Residence/Export service | datlich.html, shell Lễ tân/Nhân viên/Công an | Test máy trạng thái + path safety + hồi quy tổng |

### Ghi chú phân công

- Mỗi milestone = 1 sprint. Mở sprint mới khi sprint trước test xanh và chạy được end-to-end.
- PR review theo chuyên môn: chạm CSDL/repository → SV2 duyệt; service/nghiệp vụ → SV3; Shared/ hoặc Server/Network/ → SV1; Assets/wwwroot/ → SV4; test → SV5. Mọi PR kèm test, giữ bộ test xanh.
- Xung đột hợp đồng interface (ActionNames, DTO, schema): SV1 quyết cuối; thay đổi phải cập nhật Spec + test của milestone phụ thuộc trong cùng PR.
- Task nào cũng có SV5 nhận phần test; nghiệp vụ chính do SV3 phát triển, SV2 cung cấp repository, SV1 cung cấp protocol/router — không ai đứng ngoài bất kỳ sprint nào.
- Thành viên xong sớm ưu tiên hỗ trợ SV5 (test) hoặc SV4 (giao diện demo).

---

## XIX. Kết quả đầu ra

### 1. Source code

```
QuanLyNhaTro/
├── QuanLyTro/
│   ├── QuanLyTro.slnx
│   ├── QuanLyTro/                  # Client WinForms + WebView2
│   │   ├── Form1.cs                # Host + bridge + quyền camera
│   │   ├── Network/                # TcpClient wrapper, RoleRouter
│   │   └── Assets/wwwroot/         # login + 6 shell + datlich.html
│   ├── QuanLyTro.Server/           # Console TCP server
│   │   ├── Network/                # TcpServer, RequestRouter
│   │   ├── Security/               # SessionStore, PermissionService, PasswordHasher
│   │   ├── Services/               # Toàn bộ nghiệp vụ
│   │   ├── Repositories/           # Toàn bộ SQL repository
│   │   ├── Data/                   # Database, SchemaInitializer
│   │   └── Config/                 # ServerOptions
│   ├── QuanLyTro.Shared/           # Protocol + Models dùng chung
│   │   ├── Protocol/               # Packet, ActionNames, DefaultPermissions, JsonDefaults
│   │   └── Models/                 # DTO, Enums, Exceptions
│   └── QuanLyTro.Tests/            # MSTest tích hợp + đồng thời + bảo mật
└── Database/
    ├── schema.sql                  # 11 bảng
    └── seed_demo.sql               # Dữ liệu demo development
```

### 2. Hệ thống chạy được

- Server console lắng nghe TCP 8888; client đăng nhập đủ 6 vai trò trên dữ liệu demo.

### 3. Database

- 11 bảng schema idempotent + dữ liệu mẫu đầy đủ cho demo (khu, phòng, khách, hợp đồng, hóa đơn, sự cố, lịch).

### 4. Giao diện Demo

- Thực hiện đầy đủ thao tác theo kịch bản Mục XV, từ tạo khu đến xuất báo cáo công an.

### 5. Bộ test

- MSTest xanh: schema, RBAC, đua dữ liệu, chống replay, bất biến hóa đơn, máy trạng thái, bảo mật xuất file.

### 6. Báo cáo

- Báo cáo gồm 6 chương: Tổng quan / Cơ sở lý thuyết (TCP, RBAC, transaction) / Phân tích và thiết kế / Xây dựng hệ thống / Kiểm thử / Kết luận và hướng phát triển.

---

## XX. Hướng phát triển sau này

Phần này không thực hiện trong phiên bản hiện tại, chỉ đưa vào báo cáo như định hướng mở rộng.

### Giai đoạn tương lai 1

- Đổi giao thức TCP sang gRPC/HTTPS có TLS khi cần mở rộng đa máy thật.
- Thông báo đẩy (push) khi có hóa đơn mới/sự cố khẩn.

### Giai đoạn tương lai 2

- Tích hợp SePay thật qua webhook công khai + chữ ký HMAC.
- Thanh toán online đa kênh cho khách thuê.

### Giai đoạn tương lai 3

- App mobile (MAUI/Flutter) dùng lại cùng server TCP/protocol.
- Báo cáo tự động hằng ngày cho chủ trọ.

### Giai đoạn tương lai 4

- Kết nối dữ liệu tạm trú với hệ thống hành chính thực tế của phường (API chuẩn).
- Đa phường/đa quận, phân vùng dữ liệu.

---

## XXI. Timeline tổng thể

Với 5 sinh viên làm việc song song theo chuyên môn, dự án hoàn thành trong kế hoạch 7 tuần (5 sprint + 1 tuần tổng duyệt), nhờ các mảng CSDL/Backend/Frontend chạy đồng thời trong từng sprint.

| Tuần | Công việc | Người phụ trách | Kết quả |
|---|---|---|---|
| 1 | Phân tích + thiết kế (Spec M1–M5, ERD, packet, phân công) | Cả nhóm (5 người) | Bộ Spec + Kế hoạch này |
| 2 | Sprint M1: nền tảng TCP + tài khoản + RBAC | SV1 protocol, SV2 schema, SV3 auth, SV4 shell, SV5 test | Đăng nhập 6 vai trò chạy được |
| 3 | Sprint M2: khu trọ + phòng + người thuê + scope | SV2 schema/repo, SV3 service, SV1 router, SV4 shell, SV5 test scope | Quản lý khoanh vùng khu đúng |
| 4 | Sprint M3: hợp đồng + QR/PIN + phụ lục | SV1 generator, SV2 repo, SV3 service, SV4 UI check-in, SV5 test đua | Nhận phòng QR chạy được |
| 5 | Sprint M4: điện nước + hóa đơn + đề xuất + sự cố | SV1 attachment, SV2 7 bảng, SV3 5 service, SV4 tab thu cước, SV5 test bất biến | Vòng tiền + duyệt chạy được |
| 6 | Sprint M5: lịch kiểm tra + thống kê + lưu trú công an | SV1 public endpoint, SV2 repo, SV3 report/export, SV4 datlich + 3 shell, SV5 hồi quy tổng | 100% scope 6 vai trò |
| 7 | Tổng duyệt: demo, sửa lỗi, báo cáo 6 chương | SV1 điều phối, SV4 demo UI, SV5 test tổng, cả nhóm | Báo cáo + Demo hoàn thiện |

---

## XXII. Tiêu chí hoàn thành đề tài

Đề tài được xem là hoàn thành khi đạt 8 điều kiện sau:

1. Có hệ thống client-server TCP hoàn chỉnh: client WinForms/WebView2 không kết nối database trực tiếp.
2. Đăng nhập đúng 6 vai trò trên một bảng tai_khoan hợp nhất, lockout và PBKDF2 hoạt động đúng.
3. RBAC deny-by-default kiểm chứng bằng test: packet giả mạo vai trò nào cũng bị từ chối đúng.
4. Vòng nghiệp vụ chạy được end-to-end: khu → phòng → hợp đồng → QR/PIN nhận phòng → điện nước → hóa đơn → thu tiền → đề xuất → sự cố → lịch → thống kê → xuất lưu trú.
5. Mọi điểm đua dữ liệu có test đồng thời: hợp đồng cùng phòng, replay QR, hóa đơn cùng kỳ, duyệt đề xuất — đúng một người thắng.
6. Hóa đơn DaThu bất biến; thống kê QuanLy không chứa trường tiền; doanh thu chỉ ChuTro.
7. Bộ test MSTest xanh trên quanly_nhatro_test, hồi quy tổng M1–M5 pass.
8. Kịch bản Demo Mục XV chạy được trọn vẹn trước giảng viên.


---

## PHỤ LỤC A — Đối chiếu scope với đặc tả gốc

**Nguồn đặc tả:** `Input_Output_QuanLyQuyenHan_LuongDuLieu.xlsx` (10 tab: Tổng quan, 6 tab tác nhân AD/MG/LT/NV/TN/PM, Phan quyen & IO, Ma tran CRUD, Luong du lieu). Mọi nhóm chức năng và luồng dữ liệu dưới đây đều có nhà trong kế hoạch — không bỏ sót mục nào.

### A.1. Ánh xạ nhóm chức năng 6 tác nhân → Phân hệ / Milestone

| Tác nhân | Nhóm chức năng (Excel) | Phân hệ | Milestone |
|---|---|---|---|
| **ChuTro (AD)** | AD-01 Đăng nhập & tài khoản cá nhân; AD-02 Quản lý tài khoản | 1 | M1 |
| | AD-03 Quản lý khu trọ; AD-07 Tìm kiếm người thuê | 2 | M2 |
| | AD-06 Phụ lục hợp đồng & người ở chung | 3 | M3 |
| | AD-04 Duyệt đề xuất (tầng 1 — độc quyền) | 4 | M4 |
| | AD-05 Thống kê chiến lược / doanh thu (độc quyền) | 5 | M5 |
| **QuanLy (MG)** | MG-01 Đăng nhập | 1 | M1 |
| | MG-02 Quản lý phòng; MG-03 Quản lý người thuê; MG-10 Tìm kiếm người thuê | 2 | M2 |
| | MG-04 Quản lý hợp đồng; MG-11 Phụ lục hợp đồng | 3 | M3 |
| | MG-05 Điện nước; MG-06 Hóa đơn & thanh toán; MG-09 Quản lý sự cố | 4 | M4 |
| | MG-07 Thống kê vận hành (không doanh thu); MG-08 Báo cáo lưu trú (dùng chung Công an) | 5 | M5 |
| **LeTan (LT)** | LT-01 Đăng nhập & phiên | 1 | M1 |
| | LT-02 Duyệt lịch kiểm tra phòng; LT-03 Tra cứu phòng tư vấn | 5 | M5 |
| **NhanVien (NV)** | NV-01 Đăng nhập & phiên (kèm chuyên môn) | 1 | M1 |
| | NV-02 Việc được giao (Nhận việc / Hoàn thành + kết quả) | 5 | M5 |
| **KhachThue (TN)** | TN-01 Đăng nhập (tên = CCCD); TN-02 Xem thông tin cá nhân | 1–2 | M1–M2 |
| | TN-03 Xác nhận phòng đang ở (QR/PIN); TN-04 Xem phòng đang ở | 3 | M3 |
| | TN-05 Xem chỉ số điện nước; TN-06 Xem hóa đơn & thanh toán; TN-07 Báo cáo sự cố; TN-08 Tra cứu | 4 | M4 |
| **CongAn (PM)** | PM-01 Đăng nhập & phiên (chỉ đọc) | 1 | M1 |
| | PM-02 Tra cứu phòng trọ; PM-03 Tra cứu công dân; PM-04 Báo cáo lưu trú & tạm trú | 5 | M5 |

### A.2. Ánh xạ 26 luồng dữ liệu (DF) → Milestone

| Luồng | DF | Milestone |
|---|---|---|
| Đăng nhập, Đăng xuất, Đổi mật khẩu, Kiểm tra quyền truy cập | DF-01, DF-02, DF-03, DF-20 | M1 |
| Quản lý tài khoản nhân sự; Quản lý khu trọ & phân công | DF-04, DF-05 | M2 (CRUD khu/tài khoản: M1) |
| Quản lý phòng; Người thuê & chuyển/trả phòng; Tìm kiếm người thuê | DF-06, DF-07, DF-26 | M2 |
| Lập/gia hạn hợp đồng; Phụ lục & người ở chung; Xác nhận phòng bằng QR/mã số | DF-08, DF-25, DF-15 | M3 |
| Ghi chỉ số điện nước; Lập hóa đơn; Xác nhận thanh toán; Tra cứu hóa đơn của mình | DF-10, DF-11, DF-12, DF-14 | M4 |
| Đề xuất chấm dứt/xóa hợp đồng; Đề xuất xóa hóa đơn; Duyệt đề xuất & thông báo | DF-09, DF-13, DF-17 | M4 |
| Gửi và xử lý sự cố | DF-16 | M4 |
| Đăng nhập nhân sự (lễ tân/nhân viên); Đặt lịch xem phòng (public) | DF-21, DF-22 | M5 (lễ tân duyệt + gán việc) |
| Lễ tân duyệt & gán nhân viên; Nhân viên nhận việc & hoàn thành | DF-23, DF-24 | M5 |
| Báo cáo lưu trú & tra cứu công dân; Thống kê vận hành/chiến lược | DF-18, DF-19 | M5 |

### A.3. Quy ước phân quyền Excel → cơ chế server

| Ký hiệu Excel | Ý nghĩa | Cơ chế thực thi |
|---|---|---|
| ● Toàn quyền | Chủ trọ hoặc quyền độc quyền tầng 1 | `AllowedRoles` chứa vai trò; hành động độc quyền (AD-04, AD-05) chỉ khai báo đúng 1 vai trò |
| ◐ Theo phạm vi được gán | Quản lý/người thuê trong scope | Server lấy `khu_tro_id` / `tai_khoan_id` từ **session**, không nhận từ payload; mọi SELECT/UPDATE thêm `WHERE khu_tro_id = @scope` |
| ○ Chỉ đọc | Lễ tân tra cứu, Công an, public | Action chỉ có handler đọc; mọi action ghi server từ chối ở router trước khi chạm service |

Đối chiếu hoàn tất: **31/31 nhóm chức năng** và **26/26 luồng dữ liệu** của workbook đều có phân hệ + milestone sở hữu (bảng trên), khớp ma trận CRUD tab `Ma tran CRUD` (xóa khu chỉ khi 0 phòng, xóa phòng/hợp đồng qua đề xuất ◐, hóa đơn sửa/xóa ◐ qua đề xuất — đã phản ánh ở Phân hệ 2 và 4).
