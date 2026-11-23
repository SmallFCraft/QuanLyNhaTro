using System.Text;
using System.Text.Json;
using QuanLyTro.Server.Data;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Models;

namespace QuanLyTro.Server.Services;

/// <summary>
/// Nghiệp vụ tra cứu hồ sơ tạm trú & lịch sử cư trú cho Công an / Chủ trọ (CHỈ ĐỌC):
/// - XUAT_HO_SO_TAM_TRU: tra cứu công dân đang lưu trú, kiểm tra tiêu chí >= 4 ký tự (CCCD/họ tên/SĐT/từ khóa).
/// - LICH_SU_CU_TRU_LAY: xem lịch sử biến động cư trú (NhanPhong, ChuyenDen, ChuyenDi, TraPhong).
/// </summary>
public sealed class LuuTruService
{
    private readonly Database _db;

    public LuuTruService(Database db) => _db = db;

    public async Task<object> XuatHoSoTamTruAsync(SessionInfo session, JsonElement data)
    {
        if (session.VaiTro is not (VaiTroNguoiDung.CongAn or VaiTroNguoiDung.ChuTro))
            throw new NotFoundOrDeniedException();

        var cccd = ValidateCriterion(data, "cccd", minLength: 4, "CCCD");
        var hoTen = ValidateCriterion(data, "hoTen", minLength: 4, "Họ tên");
        var soDienThoai = ValidateCriterion(data, "soDienThoai", minLength: 4, "Số điện thoại");
        var tuKhoa = ValidateCriterion(data, "tuKhoa", minLength: 4, "Từ khóa");
        var soPhong = ValidateCriterion(data, "soPhong", minLength: 1, "Số phòng");

        var rows = await _db.QueryAsync(
            @"SELECT tk.id, tk.ho_ten, hs.cccd, hs.ngay_sinh, hs.so_dien_thoai, hs.que_quan,
                     hs.da_dang_ky_tam_tru, hs.ngay_tao, hs.ngay_cap_nhat,
                     p.id AS phong_id, p.so_phong, k.id AS khu_tro_id, k.ten_khu_tro AS ten_khu,
                     COALESCE(
                         (SELECT MAX(ls.thoi_diem) FROM lich_su_cu_tru ls
                          WHERE ls.tai_khoan_khach_id = hs.tai_khoan_id
                            AND ls.phong_id = hs.phong_id
                            AND ls.loai_bien_dong IN ('NhanPhong', 'ChuyenDen')),
                         hs.ngay_cap_nhat,
                         hs.ngay_tao
                     ) AS ngay_bat_dau_tam_tru
              FROM ho_so_khach_thue hs
              JOIN tai_khoan tk ON tk.id = hs.tai_khoan_id
              JOIN phong p ON p.id = hs.phong_id
              JOIN khu_tro k ON k.id = p.khu_tro_id
              WHERE (@cccd IS NULL OR hs.cccd LIKE @cccd ESCAPE '!')
                AND (@hoTen IS NULL OR tk.ho_ten LIKE @hoTen ESCAPE '!')
                AND (@sdt IS NULL OR hs.so_dien_thoai LIKE @sdt ESCAPE '!')
                AND (@soPhong IS NULL OR p.so_phong = @soPhongExact OR p.so_phong LIKE @soPhong ESCAPE '!')
                AND (@tuKhoa IS NULL
                     OR tk.ho_ten LIKE @tuKhoa ESCAPE '!'
                     OR hs.cccd LIKE @tuKhoa ESCAPE '!'
                     OR hs.so_dien_thoai LIKE @tuKhoa ESCAPE '!'
                     OR p.so_phong LIKE @tuKhoa ESCAPE '!')
              ORDER BY k.id, p.so_phong, tk.ho_ten",
            new
            {
                cccd = cccd is null ? null : $"%{EscapeLike(cccd)}%",
                hoTen = hoTen is null ? null : $"%{EscapeLike(hoTen)}%",
                sdt = soDienThoai is null ? null : $"%{EscapeLike(soDienThoai)}%",
                soPhongExact = soPhong,
                soPhong = soPhong is null ? null : $"%{EscapeLike(soPhong)}%",
                tuKhoa = tuKhoa is null ? null : $"%{EscapeLike(tuKhoa)}%",
            });

        var items = rows.Select(r => new
        {
            id = Convert.ToInt32(r["id"]),
            hoTen = (string)r["ho_ten"]!,
            cccd = (string)r["cccd"]!,
            soPhong = (string)r["so_phong"]!,
            tenKhu = (string)r["ten_khu"]!,
            ngayBatDauTamTru = Convert.ToDateTime(r["ngay_bat_dau_tam_tru"]!).ToString("yyyy-MM-dd"),
            soDienThoai = (string)r["so_dien_thoai"]!,
            ngaySinh = Convert.ToDateTime(r["ngay_sinh"]!).ToString("yyyy-MM-dd"),
            queQuan = (string)r["que_quan"]!,
            daDangKyTamTru = Convert.ToInt32(r["da_dang_ky_tam_tru"]) == 1,
        }).ToArray();

        var format = AccountService.GetString(data, "format") ?? AccountService.GetString(data, "dinhDang");
        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            return BuildCsv(items.Select(x => (x.hoTen, x.cccd, x.soPhong, x.tenKhu, x.ngayBatDauTamTru, x.soDienThoai)));
        }

        return items;
    }

    public async Task<object> LichSuCuTruLayAsync(SessionInfo session, JsonElement data)
    {
        if (session.VaiTro is not (VaiTroNguoiDung.CongAn or VaiTroNguoiDung.ChuTro))
            throw new NotFoundOrDeniedException();

        var khachThueId = AccountService.GetOptionalInt(data, "khachThueId")
            ?? AccountService.GetOptionalInt(data, "taiKhoanId");
        var phongId = AccountService.GetOptionalInt(data, "phongId");
        var soPhong = AccountService.GetString(data, "soPhong")?.Trim();
        if (string.IsNullOrEmpty(soPhong)) soPhong = null;
        var loaiBienDong = AccountService.GetString(data, "loaiBienDong")?.Trim();
        if (string.IsNullOrEmpty(loaiBienDong)) loaiBienDong = null;

        DateTime? tuNgay = null;
        var fromStr = AccountService.GetString(data, "from") ?? AccountService.GetString(data, "tuNgay");
        if (!string.IsNullOrWhiteSpace(fromStr) && DateTime.TryParse(fromStr, out var parsedFrom))
            tuNgay = parsedFrom.Date;

        DateTime? denNgay = null;
        var toStr = AccountService.GetString(data, "to") ?? AccountService.GetString(data, "denNgay");
        if (!string.IsNullOrWhiteSpace(toStr) && DateTime.TryParse(toStr, out var parsedTo))
            denNgay = parsedTo.Date.AddDays(1).AddTicks(-1);

        var rows = await _db.QueryAsync(
            @"SELECT ls.id, ls.tai_khoan_khach_id, tk.ho_ten, hs.cccd, hs.so_dien_thoai,
                     ls.phong_id, p.so_phong, ls.khu_tro_id, k.ten_khu_tro AS ten_khu,
                     ls.loai_bien_dong, ls.ghi_chu, ls.thoi_diem
              FROM lich_su_cu_tru ls
              JOIN tai_khoan tk ON tk.id = ls.tai_khoan_khach_id
              LEFT JOIN ho_so_khach_thue hs ON hs.tai_khoan_id = ls.tai_khoan_khach_id
              JOIN phong p ON p.id = ls.phong_id
              JOIN khu_tro k ON k.id = ls.khu_tro_id
              WHERE (@khachId IS NULL OR ls.tai_khoan_khach_id = @khachId)
                AND (@phongId IS NULL OR ls.phong_id = @phongId)
                AND (@soPhong IS NULL OR p.so_phong = @soPhong)
                AND (@loai IS NULL OR ls.loai_bien_dong = @loai)
                AND (@tuNgay IS NULL OR ls.thoi_diem >= @tuNgay)
                AND (@denNgay IS NULL OR ls.thoi_diem <= @denNgay)
              ORDER BY ls.thoi_diem DESC, ls.id DESC",
            new
            {
                khachId = khachThueId,
                phongId,
                soPhong,
                loai = loaiBienDong,
                tuNgay,
                denNgay,
            });

        return rows.Select(r =>
        {
            var dt = Convert.ToDateTime(r["thoi_diem"]!);
            return new
            {
                id = Convert.ToInt64(r["id"]),
                khachThueId = Convert.ToInt32(r["tai_khoan_khach_id"]),
                taiKhoanKhachId = Convert.ToInt32(r["tai_khoan_khach_id"]),
                hoTen = (string)r["ho_ten"]!,
                cccd = r["cccd"] as string ?? "",
                soDienThoai = r["so_dien_thoai"] as string ?? "",
                phongId = Convert.ToInt32(r["phong_id"]),
                soPhong = (string)r["so_phong"]!,
                khuTroId = Convert.ToInt32(r["khu_tro_id"]),
                tenKhu = (string)r["ten_khu"]!,
                loaiBienDong = (string)r["loai_bien_dong"]!,
                ghiChu = r["ghi_chu"] as string,
                thoiDiem = dt.ToString("yyyy-MM-ddTHH:mm:ss"),
                ngayBienDong = dt.ToString("yyyy-MM-dd"),
            };
        }).ToArray();
    }

    private static string? ValidateCriterion(JsonElement data, string propName, int minLength, string label)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(propName, out var el))
            return null;
        if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (el.ValueKind != JsonValueKind.String)
            throw new ValidationException($"Trường {propName} không hợp lệ.");

        var raw = el.GetString()?.Trim() ?? "";
        if (raw.Length == 0)
        {
            // Nếu client chỉ truyền đúng trường này mà để rỗng (VD: { cccd: "" }) thì báo lỗi validation
            var propCount = 0;
            foreach (var _ in data.EnumerateObject()) propCount++;
            if (propCount == 1)
                throw new ValidationException($"{label} tra cứu phải có ít nhất {minLength} ký tự.");
            return null;
        }
        if (raw.Length < minLength)
            throw new ValidationException($"{label} tra cứu phải có ít nhất {minLength} ký tự.");
        if (raw.Length > 100)
            throw new ValidationException($"{label} tra cứu quá dài.");
        return raw;
    }

    private static string EscapeLike(string input) =>
        input.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_").Replace("[", "![");

    private static string BuildCsv(IEnumerable<(string HoTen, string Cccd, string SoPhong, string TenKhu, string NgayBatDau, string Sdt)> rows)
    {
        static string Cell(string? v) => $"\"{(v ?? "").Replace("\"", "\"\"")}\"";
        var sb = new StringBuilder();
        sb.AppendLine("hoTen,cccd,soPhong,tenKhu,ngayBatDauTamTru,soDienThoai");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",",
                Cell(r.HoTen), Cell(r.Cccd), Cell(r.SoPhong), Cell(r.TenKhu), Cell(r.NgayBatDau), Cell(r.Sdt)));
        }
        return sb.ToString();
    }
}
