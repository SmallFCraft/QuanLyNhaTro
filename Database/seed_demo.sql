-- =============================================================
-- QuanLyNhaTro — dữ liệu demo (chạy SAU khi server khởi tạo schema)
-- Dùng cho dev/demo, không dùng production.
-- Chạy: mysql -h 127.0.0.1 -u root < Database/seed_demo.sql
-- =============================================================
USE quanly_nhatro;

-- Khu trọ
INSERT INTO khu_tro (ten_khu_tro, dia_chi)
SELECT 'Khu A - Nguyễn Hữu Thọ', 'Nguyễn Hữu Thọ, Hòa Quý, Ngũ Hành Sơn' FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM khu_tro WHERE ten_khu_tro LIKE 'Khu A%');
INSERT INTO khu_tro (ten_khu_tro, dia_chi)
SELECT 'Khu B - Trường Chinh', 'Trường Chinh, Hòa Hải, Ngũ Hành Sơn' FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM khu_tro WHERE ten_khu_tro LIKE 'Khu B%');

-- Gán quanly cho Khu A (nếu tài khoản tồn tại và khu chưa có quản lý)
UPDATE khu_tro k JOIN tai_khoan t ON t.ten_dang_nhap = 'quanly' AND t.vai_tro = 'QuanLy'
SET k.quan_ly_tai_khoan_id = t.id, t.khu_tro_id = k.id
WHERE k.ten_khu_tro LIKE 'Khu A%' AND k.quan_ly_tai_khoan_id IS NULL;

-- Phòng mẫu khu A
INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da, trang_thai)
SELECT k.id, p.so_phong, p.gia, p.suc, 'Trong' FROM
(SELECT (SELECT id FROM khu_tro WHERE ten_khu_tro LIKE 'Khu A%' LIMIT 1) khu_id) k
JOIN (SELECT '101' so_phong, 2500000 gia, 3 suc UNION ALL
      SELECT '102', 2500000, 3 UNION ALL
      SELECT '201', 2800000, 4 UNION ALL
      SELECT '202', 2800000, 4) p
WHERE NOT EXISTS (SELECT 1 FROM phong WHERE so_phong = p.so_phong);

-- Cấu hình nhận tiền mẫu (không có api_token thật)
INSERT INTO cau_hinh_he_thong (id, so_tai_khoan, ten_ngan_hang, chu_tai_khoan)
SELECT 1, '0071000999', 'VCB', 'NGUYEN VAN A' FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM cau_hinh_he_thong WHERE id = 1);
