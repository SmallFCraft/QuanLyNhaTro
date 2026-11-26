using QuanLyTro.Shared.Models;

namespace QuanLyTro.Shared.Protocol;

/// <summary>
/// Ma tran quyen mac dinh vai tro -> hanh dong. Deny-by-default:
/// cap khong co trong tu dien nghia la bi tu choi.
/// </summary>
public static class DefaultPermissions
{
    private static readonly HashSet<string> AllActions = BuildAllActions();

    private static readonly Dictionary<VaiTroNguoiDung, HashSet<string>> Matrix = BuildMatrix();

    public static bool Allowed(VaiTroNguoiDung role, string action) =>
        Matrix.TryGetValue(role, out var set) && set.Contains(action);

    public static bool IsRegisteredAction(string action) => AllActions.Contains(action);

    public static IReadOnlyCollection<string> ActionsFor(VaiTroNguoiDung role) =>
        Matrix.TryGetValue(role, out var set) ? set.ToArray() : Array.Empty<string>();

    public static IReadOnlyCollection<string> AllActionNames => AllActions;

    private static HashSet<string> BuildAllActions()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in typeof(ActionNames).GetFields())
            if (f.GetValue(null) is string s) set.Add(s);
        return set;
    }

    private static Dictionary<VaiTroNguoiDung, HashSet<string>> BuildMatrix()
    {
        var all = AllActions;
        var m = new Dictionary<VaiTroNguoiDung, HashSet<string>>();

        // ChuTro: moi hanh dong dang ky trong ActionNames (bypass quan tri theo spec M1 §4).
        m[VaiTroNguoiDung.ChuTro] = new HashSet<string>(all, StringComparer.Ordinal);

        // QuanLy: nghiep vu khu/phong/nguoi thue + tai khoan ca nhan; KHONG quan tri tai khoan,
        // KHONG phan quyen, KHONG cau hinh day du, KHONG bat ky action doanh thu (M5 se deny o router).
        var ql = new HashSet<string>(StringComparer.Ordinal)
        {
            ActionNames.DANG_NHAP, ActionNames.DANG_XUAT, ActionNames.DOI_MAT_KHAU,
            ActionNames.CAU_HINH_LAY,
            ActionNames.TAI_KHOAN_DAT_LAI_MAT_KHAU,
            ActionNames.KHU_TRO_LAY_TAT_CA,
            ActionNames.PHONG_LAY_TAT_CA, ActionNames.PHONG_LAY_CHI_TIET,
            ActionNames.PHONG_THEM, ActionNames.PHONG_CAP_NHAT,
            ActionNames.KHACH_THUE_THEM, ActionNames.KHACH_THUE_CAP_NHAT,
            ActionNames.KHACH_THUE_CAP_NHAT_TAM_TRU, ActionNames.KHACH_THUE_CHUYEN_PHONG,
            ActionNames.KHACH_THUE_TRA_PHONG, ActionNames.KHACH_THUE_TIM_KIEM,
            ActionNames.KHACH_THUE_LAY_THEO_PHONG,
            // M3: hop dong trong khu duoc gan
            ActionNames.HopDongTao, ActionNames.HopDongGiaHan,
            ActionNames.HopDongLayTatCa, ActionNames.HopDongSinhQr,
            ActionNames.PhuLucLay, ActionNames.PhuLucCapNhat,
            // M4a: dien nuoc + hoa don khu cua minh (scope kiem tra trong service)
            ActionNames.DIEN_NUOC_LAY_KY_TRUOC, ActionNames.DIEN_NUOC_GHI_SO,
            ActionNames.HOA_DON_TAO, ActionNames.HOA_DON_LAY_TAT_CA,
            ActionNames.HOA_DON_THANH_TOAN, ActionNames.DOI_SOAT_CHAY,
            ActionNames.LichKiemTraLay, ActionNames.LichKiemTraDuyet, ActionNames.LichKiemTraTuChoi, ActionNames.LichKiemTraNhanVien, ActionNames.LichKiemTraGanNv,
            // M4b: de xuat + su co (KHONG co BAO_CAO_TONG_QUAN — chi ChuTro thay tien)
            ActionNames.DeXuatTao, ActionNames.DeXuatLayTatCa,
            ActionNames.SuCoTao, ActionNames.SuCoLayTheoKhu, ActionNames.SuCoCapNhatTrangThai,
        };
        m[VaiTroNguoiDung.QuanLy] = ql;

        // LeTan: dang nhap + doi mat khau + tra cuu phong chi doc (phuc vu tu van M5).
        m[VaiTroNguoiDung.LeTan] = new HashSet<string>(StringComparer.Ordinal)
        {
            ActionNames.DANG_NHAP, ActionNames.DANG_XUAT, ActionNames.DOI_MAT_KHAU,
            ActionNames.PHONG_LAY_TAT_CA, ActionNames.PHONG_LAY_CHI_TIET,
            ActionNames.LichKiemTraLay, ActionNames.LichKiemTraDuyet, ActionNames.LichKiemTraTuChoi, ActionNames.LichKiemTraNhanVien, ActionNames.LichKiemTraGanNv,
            // M4b: le tan bao su co + xem su co khu
            ActionNames.SuCoTao, ActionNames.SuCoLayTheoKhu,
        };

        // NhanVien: hanh dong ca nhan. (M5 se cong action lam viec.)
        m[VaiTroNguoiDung.NhanVien] = new HashSet<string>(StringComparer.Ordinal)
        {
            ActionNames.DANG_NHAP, ActionNames.DANG_XUAT, ActionNames.DOI_MAT_KHAU,
            ActionNames.LichKiemTraCuaToi, ActionNames.LichKiemTraNhanViec, ActionNames.LichKiemTraHoanThanh,
            // M4b: nhan vien gui de xuat + bao su co + xem su co khu
            ActionNames.DeXuatTao, ActionNames.SuCoTao, ActionNames.SuCoLayTheoKhu,
        };

        // KhachThue: ho so + nghiep vu cua chinh minh.
        m[VaiTroNguoiDung.KhachThue] = new HashSet<string>(StringComparer.Ordinal)
        {
            ActionNames.DANG_NHAP, ActionNames.DANG_XUAT, ActionNames.DOI_MAT_KHAU,
            ActionNames.CAU_HINH_LAY, // đọc cấu hình SePay để vẽ QR thanh toán cho khách
            ActionNames.KHACH_THUE_HO_SO_CUA_TOI, ActionNames.KHACH_THUE_CAP_NHAT_LIEN_HE,
            ActionNames.KHACH_THUE_LAY_THEO_PHONG,
            // M3: nhan phong bang QR/PIN (BR-19) — duy nhat action hop dong cua KhachThue
            ActionNames.KhachThueNhanPhongQr,
            // M4a: khach thue chi xem hoa don phong cua minh
            ActionNames.HOA_DON_CUA_TOI,
            // M4b: khach thue gui de xuat + bao su co phong cua minh
            ActionNames.DeXuatTao, ActionNames.SuCoTao,
        };

        // CongAn: chi doc phong/phu luc tra cuu (M5 se cong luu tru chi doc).
        m[VaiTroNguoiDung.CongAn] = new HashSet<string>(StringComparer.Ordinal)
        {
            ActionNames.DANG_NHAP, ActionNames.DANG_XUAT, ActionNames.DOI_MAT_KHAU,
            ActionNames.PHONG_LAY_TAT_CA, ActionNames.PHONG_LAY_CHI_TIET,
            ActionNames.XuatHoSoTamTru, ActionNames.LichSuCuTruLay,
            ActionNames.KHACH_THUE_LAY_THEO_PHONG,
        };

        return m;
    }
}
