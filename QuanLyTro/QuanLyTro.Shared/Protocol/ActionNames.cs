namespace QuanLyTro.Shared.Protocol;

public static class ActionNames
{
    // M1 - xac thuc / tai khoan / phan quyen / cau hinh
    public const string DANG_NHAP = "DANG_NHAP";
    public const string DANG_XUAT = "DANG_XUAT";
    public const string DOI_MAT_KHAU = "DOI_MAT_KHAU";
    public const string TAI_KHOAN_LAY_TAT_CA = "TAI_KHOAN_LAY_TAT_CA";
    public const string TAI_KHOAN_TAO = "TAI_KHOAN_TAO";
    public const string TAI_KHOAN_CAP_NHAT = "TAI_KHOAN_CAP_NHAT";
    public const string TAI_KHOAN_KHOA = "TAI_KHOAN_KHOA";
    public const string TAI_KHOAN_MO_KHOA = "TAI_KHOAN_MO_KHOA";
    public const string TAI_KHOAN_DAT_LAI_MAT_KHAU = "TAI_KHOAN_DAT_LAI_MAT_KHAU";
    public const string TAI_KHOAN_XOA = "TAI_KHOAN_XOA";
    public const string PHAN_QUYEN_LAY_MA_TRAN = "PHAN_QUYEN_LAY_MA_TRAN";
    public const string PHAN_QUYEN_CAP_NHAT_VAI_TRO = "PHAN_QUYEN_CAP_NHAT_VAI_TRO";
    public const string CAU_HINH_LAY = "CAU_HINH_LAY";
    public const string CAU_HINH_DAY_DU = "CAU_HINH_DAY_DU";
    public const string CAU_HINH_CAP_NHAT = "CAU_HINH_CAP_NHAT";

    // M2 - khu tro / phong / nguoi thue
    public const string KHU_TRO_LAY_TAT_CA = "KHU_TRO_LAY_TAT_CA";
    public const string KHU_TRO_THEM = "KHU_TRO_THEM";
    public const string KHU_TRO_CAP_NHAT = "KHU_TRO_CAP_NHAT";
    public const string KHU_TRO_PHAN_CONG_QUAN_LY = "KHU_TRO_PHAN_CONG_QUAN_LY";
    public const string KHU_TRO_XOA = "KHU_TRO_XOA";
    public const string KHU_TRO_NGUNG_HOAT_DONG = "KHU_TRO_NGUNG_HOAT_DONG";
    public const string PHONG_LAY_TAT_CA = "PHONG_LAY_TAT_CA";
    public const string PHONG_LAY_CHI_TIET = "PHONG_LAY_CHI_TIET";
    public const string PHONG_THEM = "PHONG_THEM";
    public const string PHONG_CAP_NHAT = "PHONG_CAP_NHAT";
    public const string PHONG_XOA = "PHONG_XOA";
    public const string KHACH_THUE_DANG_KY = "KHACH_THUE_DANG_KY";
    public const string KHACH_THUE_THEM = "KHACH_THUE_THEM";
    public const string KHACH_THUE_CAP_NHAT = "KHACH_THUE_CAP_NHAT";
    public const string KHACH_THUE_CAP_NHAT_TAM_TRU = "KHACH_THUE_CAP_NHAT_TAM_TRU";
    public const string KHACH_THUE_CHUYEN_PHONG = "KHACH_THUE_CHUYEN_PHONG";
    public const string KHACH_THUE_TRA_PHONG = "KHACH_THUE_TRA_PHONG";
    public const string KHACH_THUE_XOA = "KHACH_THUE_XOA";
    public const string KHACH_THUE_TIM_KIEM = "KHACH_THUE_TIM_KIEM";
    public const string KHACH_THUE_HO_SO_CUA_TOI = "KHACH_THUE_HO_SO_CUA_TOI";
    public const string KHACH_THUE_CAP_NHAT_LIEN_HE = "KHACH_THUE_CAP_NHAT_LIEN_HE";
    public const string KHACH_THUE_LAY_THEO_PHONG = "KHACH_THUE_LAY_THEO_PHONG";

    // M3 - hop dong + qr/pin nhan phong
    public const string HopDongTao = "HOP_DONG_TAO";
    public const string HopDongGiaHan = "HOP_DONG_GIA_HAN";
    public const string HopDongChamDut = "HOP_DONG_CHAM_DUT";
    public const string HopDongLayTatCa = "HOP_DONG_LAY_TAT_CA";
    public const string HopDongSinhQr = "HOP_DONG_SINH_QR";
    public const string PhuLucLay = "PHU_LUC_LAY";
    public const string PhuLucCapNhat = "PHU_LUC_CAP_NHAT";
    public const string KhachThueNhanPhongQr = "KHACH_THUE_NHAN_PHONG_QR";

    // M4a - dien nuoc / hoa don / doi soat
    public const string DIEN_NUOC_LAY_KY_TRUOC = "DIEN_NUOC_LAY_KY_TRUOC";
    public const string DIEN_NUOC_GHI_SO = "DIEN_NUOC_GHI_SO";
    public const string HOA_DON_TAO = "HOA_DON_TAO";
    public const string HOA_DON_LAY_TAT_CA = "HOA_DON_LAY_TAT_CA";
    public const string HOA_DON_THANH_TOAN = "HOA_DON_THANH_TOAN";
    public const string HOA_DON_CUA_TOI = "HOA_DON_CUA_TOI";
    public const string DOI_SOAT_CHAY = "DOI_SOAT_CHAY";
    // M5 - dat lich public / lich kiem tra / luu tru cong an
    public const string PhongLayTrongPublic = "PHONG_LAY_TRONG_PUBLIC";
    public const string KhachThueDatLich = "KHACH_THUE_DAT_LICH";
    public const string LichKiemTraLay = "LICH_KIEM_TRA_LAY";
    public const string LichKiemTraDuyet = "LICH_KIEM_TRA_DUYET";
    public const string LichKiemTraTuChoi = "LICH_KIEM_TRA_TU_CHOI";
    public const string LichKiemTraNhanVien = "LICH_KIEM_TRA_NHAN_VIEN";
    public const string LichKiemTraGanNv = "LICH_KIEM_TRA_GAN_NV";
    public const string LichKiemTraCuaToi = "LICH_KIEM_TRA_CUA_TOI";
    public const string LichKiemTraNhanViec = "LICH_KIEM_TRA_NHAN_VIEC";
    public const string LichKiemTraHoanThanh = "LICH_KIEM_TRA_HOAN_THANH";
    public const string XuatHoSoTamTru = "XUAT_HO_SO_TAM_TRU";
    public const string LichSuCuTruLay = "LICH_SU_CU_TRU_LAY";
}
