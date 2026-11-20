# Bộ Template Glassmorphism Responsive (Desktop & Mobile) — `QuanLyTro/Assets/wwwroot/`

- **Phong cách:** Glassmorphism (nền lưới ánh sáng dịu, thẻ kính mờ `backdrop-filter: blur(18px) saturate(165%)`, viền sáng specular 1px, giữ nhận diện Navy `#1B335F` + Gold `#C8960A`, độ tương phản >= 4.5:1).
- **Responsive:** Tự đồng bộ Desktop (1440×900) và Mobile (390px) với Sidebar off-canvas drawer cho Chủ trọ/Quản lý/Công an và thanh điều hướng đáy 4 tab cho Shell Khách thuê (`khachthue/index.html`).
- **Tương thích Contract Tests:** Giữ nguyên toàn bộ ID của `khachthue/index.html` (`#khachthue`, `#tenantTabs`, `#tab-overview`, `#tab-current`, `#tab-history`, `#tab-utilities`, `#tab-nhan-phong`...). Khi ghép vào repo, giữ lại toàn bộ thư mục `js/` và `shared/js/bridge.js` gốc.
