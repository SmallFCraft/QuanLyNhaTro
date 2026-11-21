using MySqlConnector;

namespace QuanLyTro.Server.Data;

/// <summary>
/// Khởi tạo lược đồ idempotent: CREATE TABLE IF NOT EXISTS, chạy lại không mất dữ liệu.
/// 8 bảng M1+M2 theo kế hoạch 22 mục (mục IX Thiết kế dữ liệu).
/// </summary>
public sealed class SchemaInitializer
{
    private readonly Database _db;

    public SchemaInitializer(Database db) => _db = db;

    public async Task InitializeAsync()
    {
        await _db.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS tai_khoan (
    id INT AUTO_INCREMENT PRIMARY KEY,
    ten_dang_nhap VARCHAR(50) NOT NULL UNIQUE,
    mat_khau_hash VARCHAR(200) NOT NULL,
    ho_ten VARCHAR(100) NOT NULL,
    vai_tro VARCHAR(20) NOT NULL,
    khu_tro_id INT NULL,
    chuyen_mon VARCHAR(20) NULL,
    bi_khoa TINYINT NOT NULL DEFAULT 0,
    ly_do_khoa VARCHAR(200) NULL,
    ngay_tao DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ngay_cap_nhat DATETIME NULL,
    CONSTRAINT CK_tk_vaitro CHECK (vai_tro IN ('ChuTro','QuanLy','LeTan','NhanVien','KhachThue','CongAn')),
    CONSTRAINT CK_tk_khu CHECK (khu_tro_id IS NULL OR vai_tro = 'QuanLy'),
    CONSTRAINT CK_tk_mon CHECK (chuyen_mon IS NULL OR vai_tro = 'NhanVien')
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");

        await _db.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS quyen_vai_tro (
    vai_tro VARCHAR(20) NOT NULL,
    hanh_dong VARCHAR(64) NOT NULL,
    PRIMARY KEY (vai_tro, hanh_dong)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");

        await _db.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS quyen_mac_dinh_da_ap_dung (
    vai_tro VARCHAR(20) NOT NULL,
    hanh_dong VARCHAR(64) NOT NULL,
    phien_ban VARCHAR(20) NOT NULL,
    da_ap_dung_luc DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (vai_tro, hanh_dong)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");

        await _db.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS khu_tro (
    id INT AUTO_INCREMENT PRIMARY KEY,
    ten_khu_tro VARCHAR(100) NOT NULL,
    dia_chi VARCHAR(200) NOT NULL,
    quan_ly_tai_khoan_id INT NULL,
    bi_ngung_hoat_dong TINYINT NOT NULL DEFAULT 0,
    ngay_tao DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ngay_cap_nhat DATETIME NULL,
    CONSTRAINT FK_khu_ql FOREIGN KEY (quan_ly_tai_khoan_id) REFERENCES tai_khoan(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");

        await _db.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS phong (
    id INT AUTO_INCREMENT PRIMARY KEY,
    khu_tro_id INT NOT NULL,
    so_phong VARCHAR(20) NOT NULL,
    gia_thue DECIMAL(12,2) NOT NULL,
    so_nguoi_toi_da INT NOT NULL,
    trang_thai VARCHAR(10) NOT NULL DEFAULT 'Trong',
    mo_ta VARCHAR(500) NULL,
    ngay_tao DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ngay_cap_nhat DATETIME NULL,
    UNIQUE KEY UK_phong_khu_so (khu_tro_id, so_phong),
    CONSTRAINT FK_phong_khu FOREIGN KEY (khu_tro_id) REFERENCES khu_tro(id),
    CONSTRAINT CK_phong_trangthai CHECK (trang_thai IN ('Trong','DaThue','BaoTri')),
    CONSTRAINT CK_phong_gia CHECK (gia_thue > 0),
    CONSTRAINT CK_phong_succhua CHECK (so_nguoi_toi_da > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");

        await _db.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS ho_so_khach_thue (
    tai_khoan_id INT NOT NULL PRIMARY KEY,
    cccd CHAR(12) NOT NULL UNIQUE,
    ngay_sinh DATE NOT NULL,
    so_dien_thoai VARCHAR(15) NOT NULL,
    que_quan VARCHAR(200) NOT NULL,
    noi_lam_viec VARCHAR(200) NULL,
    phong_id INT NULL,
    da_dang_ky_tam_tru TINYINT NOT NULL DEFAULT 0,
    ngay_tao DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ngay_cap_nhat DATETIME NULL,
    CONSTRAINT FK_hs_tk FOREIGN KEY (tai_khoan_id) REFERENCES tai_khoan(id) ON DELETE CASCADE,
    CONSTRAINT FK_hs_phong FOREIGN KEY (phong_id) REFERENCES phong(id),
    CONSTRAINT CK_hs_cccd CHECK (CHAR_LENGTH(cccd) = 12 AND cccd REGEXP '^[0-9]+$')
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");

        await _db.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS lich_su_cu_tru (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    tai_khoan_khach_id INT NOT NULL,
    phong_id INT NOT NULL,
    khu_tro_id INT NOT NULL,
    loai_bien_dong VARCHAR(12) NOT NULL,
    ghi_chu VARCHAR(300) NULL,
    nguoi_thuc_hien_id INT NULL,
    thoi_diem DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT FK_lsct_kh FOREIGN KEY (tai_khoan_khach_id) REFERENCES tai_khoan(id),
    CONSTRAINT FK_lsct_phong FOREIGN KEY (phong_id) REFERENCES phong(id),
    CONSTRAINT FK_lsct_khu FOREIGN KEY (khu_tro_id) REFERENCES khu_tro(id),
    CONSTRAINT CK_lsct_loai CHECK (loai_bien_dong IN ('NhanPhong','ChuyenDen','ChuyenDi','TraPhong'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");

        await _db.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS cau_hinh_he_thong (
    id INT NOT NULL PRIMARY KEY,
    so_tai_khoan VARCHAR(30) NULL,
    ten_ngan_hang VARCHAR(100) NULL,
    chu_tai_khoan VARCHAR(100) NULL,
    mau_qr VARCHAR(500) NULL,
    api_token VARCHAR(200) NULL,
    ngay_cap_nhat DATETIME NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");

        // M3: hợp đồng thuê phòng
        await _db.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS hop_dong (
    id INT AUTO_INCREMENT PRIMARY KEY,
    phong_id INT NOT NULL,
    nguoi_dai_dien_id INT NOT NULL,
    ngay_bat_dau DATE NOT NULL,
    ngay_ket_thuc DATE NOT NULL,
    gia_thue DECIMAL(12,2) NOT NULL,
    tien_coc DECIMAL(12,2) NOT NULL DEFAULT 0,
    trang_thai VARCHAR(20) NOT NULL DEFAULT 'ChoNhanPhong',
    ghi_chu VARCHAR(500) NULL,
    ngay_tao DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX IX_hop_dong_phong (phong_id),
    INDEX IX_hop_dong_trangthai (trang_thai),
    CONSTRAINT FK_hd_phong FOREIGN KEY (phong_id) REFERENCES phong(id),
    CONSTRAINT FK_hd_tk FOREIGN KEY (nguoi_dai_dien_id) REFERENCES tai_khoan(id),
    CONSTRAINT CK_hd_trangthai CHECK (trang_thai IN ('ChoNhanPhong','HieuLuc','HetHan','ChamDut')),
    CONSTRAINT CK_hd_ngay CHECK (ngay_ket_thuc > ngay_bat_dau)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");

        // M3: mã nhận phòng QR/PIN dùng một lần (BR-19)
        // "chỉ 1 mã chưa dùng mỗi hợp đồng" đảm bảo ở service (vô hiệu mã cũ trước khi sinh mã mới);
        // lần quét kép bị chặn bởi FOR UPDATE + UPDATE da_su_dung trong transaction.
        await _db.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS ma_nhan_phong (
    id INT AUTO_INCREMENT PRIMARY KEY,
    hop_dong_id INT NOT NULL,
    pin VARCHAR(8) NOT NULL,
    qr_data VARCHAR(255) NOT NULL,
    het_han DATE NOT NULL,
    da_su_dung TINYINT(1) NOT NULL DEFAULT 0,
    ngay_tao DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY UK_mnp_qrdata (qr_data),
    INDEX IX_mnp_hopdong (hop_dong_id),
    CONSTRAINT FK_mnp_hd FOREIGN KEY (hop_dong_id) REFERENCES hop_dong(id),
    CONSTRAINT CK_mnp_pin CHECK (pin REGEXP '^[0-9]{8}$')
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");

        // M3: phụ lục hợp đồng (gia hạn / sửa giá / thêm người / trả phòng sớm)
        await _db.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS phu_luc_hop_dong (
    id INT AUTO_INCREMENT PRIMARY KEY,
    hop_dong_id INT NOT NULL,
    loai VARCHAR(20) NOT NULL,
    noi_dung VARCHAR(1000) NOT NULL,
    ngay_ap_dung DATE NULL,
    trang_thai VARCHAR(10) NOT NULL DEFAULT 'Nhap',
    CONSTRAINT FK_plhd_hd FOREIGN KEY (hop_dong_id) REFERENCES hop_dong(id),
    CONSTRAINT CK_plhd_loai CHECK (loai IN ('GiaHan','SuaGia','ThemNguoi','TraPhongSom')),
    CONSTRAINT CK_plhd_trangthai CHECK (trang_thai IN ('Nhap','HieuLuc'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");

        // M4a: chỉ số điện nước theo (phòng, kỳ cước yyyy-MM), UPSERT trên UNIQUE KEY
        await _db.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS chi_so_dien_nuoc (
    id INT AUTO_INCREMENT PRIMARY KEY,
    phong_id INT NOT NULL,
    ky_cuoc CHAR(7) NOT NULL,
    dien_cu INT NOT NULL,
    dien_moi INT NOT NULL,
    nuoc_cu INT NOT NULL,
    nuoc_moi INT NOT NULL,
    gia_dien BIGINT NOT NULL,
    gia_nuoc BIGINT NOT NULL,
    hinh_thuc_nuoc ENUM('Khoi','Nguoi','DauNguoi') NOT NULL DEFAULT 'Khoi',
    so_nguoi_nuoc INT NULL,
    ghi_chu VARCHAR(300) NULL,
    ngay_ghi DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY UK_csdn_phong_ky (phong_id, ky_cuoc),
    CONSTRAINT FK_csdn_phong FOREIGN KEY (phong_id) REFERENCES phong(id),
    CONSTRAINT CK_csdn_dien CHECK (dien_moi >= dien_cu),
    CONSTRAINT CK_csdn_nuoc CHECK (nuoc_moi >= nuoc_cu)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");

        // M4a: hóa đơn theo (phòng, kỳ), tiền VND nguyên (BIGINT đồng)
        await _db.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS hoa_don (
    id INT AUTO_INCREMENT PRIMARY KEY,
    phong_id INT NOT NULL,
    ky_cuoc CHAR(7) NOT NULL,
    tien_phong BIGINT NOT NULL,
    tien_dien BIGINT NOT NULL,
    tien_nuoc BIGINT NOT NULL,
    phi_khac BIGINT NOT NULL DEFAULT 0,
    tong_tien BIGINT NOT NULL,
    trang_thai ENUM('ChuaThu','DaThu') NOT NULL DEFAULT 'ChuaThu',
    ngay_dong DATETIME NULL,
    nguoi_thu_id INT NULL,
    ghi_chu VARCHAR(300) NULL,
    UNIQUE KEY UK_hd_phong_ky (phong_id, ky_cuoc),
    CONSTRAINT FK_hoadon_phong FOREIGN KEY (phong_id) REFERENCES phong(id),
    CONSTRAINT FK_hoadon_thu FOREIGN KEY (nguoi_thu_id) REFERENCES tai_khoan(id),
    CONSTRAINT CK_hd_tong CHECK (tong_tien >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");

        // FK tai_khoan.khu_tro_id -> khu_tro tạo sau khu_tro để thứ tự CREATE ổn định
        await _db.ExecuteAsync(@"SET @fk_exists = (SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE() AND table_name = 'tai_khoan' AND index_name = 'FK_tk_khu');
SET @sql = IF(@fk_exists = 0,
    'ALTER TABLE tai_khoan ADD CONSTRAINT FK_tk_khu FOREIGN KEY (khu_tro_id) REFERENCES khu_tro(id)',
    'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;");
    }
}
