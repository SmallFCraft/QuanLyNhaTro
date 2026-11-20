# PROMPT GOOGLE STITCH — GIAO DIỆN CHUẨN "QUẢN LÝ NHÀ TRỌ NGŨ HÀNH SƠN"

> Cách dùng: copy từng prompt vào Google Stitch (stitch.withgoogle.com), tạo lần lượt theo thứ tự
> 01 → 08. Mỗi prompt sinh 1 màn hình; Stitch giữ style giữa các màn trong cùng 1 theme.
> Sau khi ưng hết: Export → Figma để làm tài liệu bàn giao, rồi code HTML/CSS thật vào
> `QuanLyTro/Assets/wwwroot/` thay UI tối giản hiện tại.

---

## ⚙️ STYLE CHUNG (dán vào mục Theme/Custom style của Stitch trước, hoặc dẫn đầu mỗi prompt)

**Master prompt cho theme:**

```
Professional Vietnamese property-management desktop web app UI, 1440×900 desktop viewport.
Operate mode: dashboard app shell, dense but breathable.
Visual identity: deep navy blue #1F3864 as primary, warm gold #BF9000 as accent,
off-white #F4F6FA background, white cards with 8px radius and subtle 1px #E4E7EE borders,
soft shadows (0 2px 12px rgba(0,0,0,0.08)).
Typography: "Be Vietnam Pro" for all text; 22px page titles in navy, 13px table text,
600-weight for labels and nav items. Numbers use tabular figures.
Layout: left sidebar 240px navy with white nav items + active item highlighted with gold
left border; top userbar 56px showing user name, role badge (gold pill), notification bell,
connection status dot (green = online) and logout button.
All body copy in Vietnamese. Status badges are colored pills: Trong = green,
DaThue = amber, BaoTri = red, ChoDuyet = amber, DaDuyet = green, TuChoi = red.
Icons: Material Symbols outlined, 20px. Buttons: navy filled primary, subtle secondary.
Accessible: 4.5:1 contrast, visible focus rings, all inputs with labels.
No illustrations, no decorative 3D, no gradients — clean enterprise SaaS look.
```

---

## 01 — Màn hình Đăng nhập (login)

```
Login screen for "Quản lý nhà trọ" property management app, desktop web 1440×900.
Centered card 400px wide on off-white background, card has double border style:
outer 2px solid navy #1F3864, inner thin gold line (reminiscent of Vietnamese thesis cover).
Card content top-to-bottom: app title "QUẢN LÝ NHÀ TRỌ" in navy 24px bold caps,
subtitle "Phường Ngũ Hành Sơn — TP. Đà Nẵng" 12px gray,
labeled input "Tên đăng nhập", labeled password input "Mật khẩu" with eye toggle,
full-width navy button "Đăng nhập", error message area in red 13px.
Below the card, a small muted hint row listing demo roles as text:
Chủ trọ · Quản lý · Lễ tân · Nhân viên · Khách thuê · Công an.
Vietnamese labels, Be Vietnam Pro font, clean enterprise style, no illustration.
```

## 02 — Shell Chủ trọ (dashboard tổng)

```
Landlord dashboard "Shell Chủ trọ" of the property management app, desktop 1440×900,
using the shared navy sidebar + userbar theme (theme đã tạo ở master prompt).
Sidebar nav items top-to-bottom: Tổng quan, Khu trọ, Phòng, Hợp đồng, Điện nước,
Hóa đơn, Đề xuất, Sự cố, Tài khoản, Phân quyền, Cấu hình nhận tiền, Thống kê doanh thu.
Content grid on "Tổng quan": 4 KPI stat cards in one row —
"Số khu trọ: 3", "Tổng phòng: 48", "Tỷ lệ lấp đầy: 87%", "Doanh thu tháng: 96.400.000 đ"
(the last one shows a small gold "độc quyền" badge).
Below: a room-status donut chart card (Trong 12 · DaThue 32 · BaoTri 4),
a line chart card "Doanh thu 6 tháng" with axis labels in million VND,
and a table card "Đề xuất chờ duyệt" with 3 rows: loại đề xuất, người gửi, khu, thời gian,
and two small buttons Duyệt / Từ chối on each row.
Vietnamese everywhere, tabular numbers, Material outlined icons.
```

## 03 — Shell Quản lý (dashboard khu được gán — KHÔNG doanh thu)

```
Manager dashboard "Shell Quản lý — Khu A" desktop 1440×900, same navy sidebar theme.
Sidebar items: Tổng quan, Phòng khu tôi, Người thuê, Điện nước, Hóa đơn & thu tiền,
Đề xuất đã gửi, Sự cố, Thống kê vận hành.
NOTE: absolutely no revenue/money statistics anywhere in this screen.
Content: 4 KPI cards — "Tổng phòng: 16", "Đang thuê: 14", "Phòng còn nợ: 3 hóa đơn",
"Sự cố đang xử lý: 2" (unit is count, never money).
Below left: room grid card, each room a small tile showing room number "101" big,
status pill, tenant name under it; tiles 96×96.
Below right: "Sự cố khu tôi" list card with severity pills Thap/TrungBinh/Cao/KhanCap
and status text, plus a "Đề xuất của tôi" table with status pills ChoDuyet/DaDuyet/TuChoi.
Top userbar shows "Khu A" chip next to role badge Quản lý.
```

## 04 — Quản lý phòng (detail view dùng chung ChuTro/QuanLy)

```
Room management screen "Danh sách phòng" desktop 1440×900, same theme.
Toolbar above table: search input "Tìm số phòng…", status filter select
(Tất cả / Trong / DaThue / BaoTri), and navy button "+ Thêm phòng".
Main table columns: Số phòng (bold navy), Khu, Giá thuê (tabular "2.500.000 đ"),
Sức chứa, Người đang ở, Trạng thái (colored pill), Hành động (2 icon buttons: edit, delete).
8 sample rows mixing Trong/DaThue/BaoTri statuses.
Right side slides out an empty-state panel hint: click a room row to see detail —
placeholder with room icon and text "Chọn một phòng để xem chi tiết".
Vietnamese, clean data-dense table, row hover highlight light blue.
```

## 05 — Người thuê & tìm kiếm (kèm form đăng ký public)

```
Two-panel tenant management screen desktop 1440×900, same navy theme.
Left 60%: search bar "Tìm theo họ tên / CCCD / SĐT" with live result count
"Tìm thấy 12 / giới hạn 50", tenant table columns: Họ tên, CCCD (12 digits, tabular),
SĐT, Phòng, Tạm trú (pill Đã đăng ký green / Chưa đăng ký red), Hành động
(Chuyển phòng, Trả phòng icon buttons).
Right 40%: card "Đăng ký khách thuê mới" form fields: Họ tên, CCCD (12 số),
Ngày sinh (date input), SĐT, Quê quán, Nơi làm việc (optional), mật khẩu ban đầu,
and a submit button "Tạo tài khoản + hồ sơ" with helper text
"Tạo trong 1 giao dịch: tài khoản + hồ sơ 1-1".
Vietnamese labels, accessible field labels, focus rings gold.
```

## 06 — Hợp đồng + nhận phòng QR/PIN (M3 preview)

```
Contract & QR check-in screen desktop 1440×900, same navy theme, two tabs in content header:
"Hợp đồng" and "Nhận phòng".
Tab Hợp đồng: contract table (Mã HĐ, Phòng, Người đại diện, Ngày BĐ→KT, Giá,
trạng thái pill ChoNhanPhong/HieuLuc/HetHan/ChamDut, actions Gia hạn / In phụ lục),
plus a primary button "+ Lập hợp đồng".
Tab Nhận phòng: centered card with big QR code 200×200 rendered from data,
under it a masked PIN field "Nhập PIN 8 số" with 8 separate digit boxes,
three method icons above: Quét webcam · Chọn ảnh QR · Nhập PIN (PIN selected, gold underline),
and a note "Mã chỉ dùng một lần — hệ thống xóa mã sau khi kích hoạt".
Status flow strip at bottom: ChoNhanPhong → HieuLuc → HetHan/ChamDut with current step highlighted.
```

## 07 — Hóa đơn + thu tiền + QR chuyển khoản (M4 preview)

```
Invoice management screen desktop 1440×900, same navy theme.
Header row: month selector "Kỳ cước 2026-11", filter select trạng thái, button "+ Lập hóa đơn".
Invoice table columns: Phòng, Kỳ, Tiền phòng, Tiền điện, Tiền nước, Tổng tiền (bold tabular),
Trạng thái pill ChuaThu amber / DaThu green, Hành động (Thu tiền button disabled when DaThu
with tooltip "Hóa đơn đã thu là bất biến").
Right drawer (open): "Chi tiết hóa đơn phòng 101 — kỳ 2026-11" with line items
(tiền phòng 2.500.000, điện 420.000, nước 150.000, tổng 3.070.000 đ),
payment history list, and a QR transfer code card with bank info
"VCB 0071000999 — NGUYEN VAN A" and a copy button.
Money all in VND tabular figures, right-aligned columns.
```

## 08 — Shell Công an + Public đặt lịch (M5 preview)

```
Two screens in one request, same navy theme, desktop 1440×900.

Screen A "Shell Công an (chỉ đọc)": sidebar items Tra cứu công dân, Tạm trú,
Chưa đăng ký, Thống kê phường, Xuất báo cáo. Content: search panel with 4 inputs
(CCCD ≥4 số, Họ tên, SĐT, Số phòng) and a "Tra cứu" button; results table with
resident rows (Họ tên, CCCD, Phòng, Tạm trú status); a gold "Xuất Excel" button
top-right of the table. NO edit/create/delete buttons anywhere — read-only enforcement visible.

Screen B "Trang public đặt lịch (không cần đăng nhập)": light marketing-style layout,
header with app name, hero section "Xem phòng trọ quanh Ngũ Hành Sơn",
grid of vacant-room cards (room number big, price "2.500.000 đ/tháng", pill Trống,
button "Đặt lịch xem"), and a booking form card: Họ tên, SĐT, Chọn phòng (select),
Thời gian (datetime input), submit "Gửi yêu cầu" with helper note
"Không quá 3 yêu cầu chờ duyệt mỗi ngày từ một số điện thoại".
```

---

## Quy trình sau Stitch

1. Export Figma → đối chiếu với `docs/design/DESIGN.md` (nếu có).
2. Code thật vào `QuanLyTro/Assets/wwwroot/`: mỗi shell thay HTML/CSS hiện tại,
   giữ nguyên `api.js` (bridge requestId) — chỉ đổi presentation, không đổi hợp đồng action.
3. Chạy lại UI contract tests (`dotnet test --filter UiContractTests`) để chắc bridge không vỡ.
4. Ưu tiên dán từng screen Stitch sinh ra so với ASCII sketch trong kế hoạch 22 mục (mục XIII).
