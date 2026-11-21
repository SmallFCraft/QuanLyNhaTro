using System.Text.Json;
using QuanLyTro.Server.Network;
using QuanLyTro.Server.Repositories;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Models;

namespace QuanLyTro.Server.Services;

/// <summary>
/// M4a: lập hóa đơn từ chỉ số đã chốt + thu tiền (BR-11). Tiền phòng đọc từ hợp đồng HieuLuc;
/// M3 hop_dong chưa merge nên fallback gia_thue từ bảng phong — TODO hợp nhất sau M3.
/// QuanLy chỉ thấy/thu hóa đơn khu của mình. Hóa đơn đã thu bất biến.
/// </summary>
public sealed class HoaDonService
{
    private readonly HoaDonRepository _repo;
    private readonly DienNuocRepository _dienNuoc;
    private readonly RoomRepository _rooms;

    public HoaDonService(HoaDonRepository repo, DienNuocRepository dienNuoc, RoomRepository rooms)
    {
        _repo = repo;
        _dienNuoc = dienNuoc;
        _rooms = rooms;
    }

    public async Task<object> TaoAsync(SessionInfo session, JsonElement data)
    {
        var phongId = AccountService.GetInt(data, "phongId");
        var kyCuoc = LayKyCuoc(data);
        await RequirePhongScopeAsync(session, phongId);

        var phong = await _rooms.RoomByIdAsync(phongId) ?? throw new NotFoundOrDeniedException();

        if (await _repo.CountTrungAsync(phongId, kyCuoc) > 0)
            throw new BusinessRuleException("Phòng này đã có hóa đơn cho kỳ cước trên.");

        var chiSo = await _dienNuoc.ByKyAsync(phongId, kyCuoc)
            ?? throw new BusinessRuleException("Chưa có chỉ số điện nước cho kỳ này — phải chốt chỉ số trước khi lập hóa đơn.");

        var dienCu = Convert.ToInt32(chiSo["dien_cu"]);
        var dienMoi = Convert.ToInt32(chiSo["dien_moi"]);
        var nuocCu = Convert.ToInt32(chiSo["nuoc_cu"]);
        var nuocMoi = Convert.ToInt32(chiSo["nuoc_moi"]);
        var giaDien = Convert.ToInt64(chiSo["gia_dien"]);
        var giaNuoc = Convert.ToInt64(chiSo["gia_nuoc"]);
        var hinhThucNuoc = (string)chiSo["hinh_thuc_nuoc"]!;
        var soNguoiNuoc = chiSo["so_nguoi_nuoc"] is null or DBNull ? 0 : Convert.ToInt32(chiSo["so_nguoi_nuoc"]);

        var phiKhac = GetOptionalLong(data, "phiKhac") ?? 0;
        if (phiKhac < 0) throw new ValidationException("Phí khác không được âm.");

        var tienPhong = await TienPhongAsync(phongId, phong);
        var tienDien = (dienMoi - dienCu) * giaDien;
        var tienNuoc = hinhThucNuoc == "Khoi"
            ? (nuocMoi - nuocCu) * giaNuoc
            : soNguoiNuoc * giaNuoc; // Nguoi/DauNguoi: đầu người × giá
        var tongTien = tienPhong + tienDien + tienNuoc + phiKhac;

        var id = await _repo.InsertAsync(phongId, kyCuoc, tienPhong, tienDien, tienNuoc,
            phiKhac, tongTien, null);
        return new
        {
            hoaDonId = id, phongId, kyCuoc, tienPhong, tienDien, tienNuoc, phiKhac, tongTien,
            trangThai = "ChuaThu",
        };
    }

    /// <summary>
    /// Tiền phòng: hợp đồng HieuLuc của phòng (M3) nếu có, fallback gia_thue phong.
    /// TODO(M3-merge): khi hợp đồng merge, ưu tiên hop_dong.gia_thue cho kỳ cước nằm trong hạn.
    /// </summary>
    private async Task<long> TienPhongAsync(int phongId, Dictionary<string, object?> phong)
    {
        var giaThue = phong["gia_thue"] is null or DBNull ? 0L : Convert.ToInt64(phong["gia_thue"]);
        return await Task.FromResult(giaThue);
    }

    public async Task<object> LayTatCaAsync(SessionInfo session, JsonElement data)
    {
        var kyCuoc = AccountService.GetString(data, "kyCuoc");
        var phongIdRaw = AccountService.GetOptionalInt(data, "phongId");

        int? khuTroId = null;
        if (session.VaiTro == VaiTroNguoiDung.QuanLy)
        {
            if (session.KhuTroId is null) throw new NotFoundOrDeniedException();
            khuTroId = session.KhuTroId;
        }

        var rows = await _repo.ListAsync(kyCuoc, phongIdRaw);
        var result = new List<object>();
        foreach (var r in rows)
        {
            var khu = Convert.ToInt32(r["khu_tro_id"]!);
            if (khuTroId is not null && khu != khuTroId) continue;
            result.Add(HoaDonDto(r));
        }
        return result;
    }

    public async Task<object> ThanhToanAsync(SessionInfo session, JsonElement data)
    {
        var id = AccountService.GetInt(data, "hoaDonId");
        var hd = await _repo.ByIdAsync(id) ?? throw new NotFoundOrDeniedException();
        if (session.VaiTro == VaiTroNguoiDung.QuanLy &&
            session.KhuTroId != Convert.ToInt32(hd["khu_tro_id"]!))
            throw new NotFoundOrDeniedException();
        if ((string)hd["trang_thai"]! == "DaThu")
            throw new BusinessRuleException("Hóa đơn đã thu rồi, không thể thu lại (BR-11).");

        var n = await _repo.MarkPaidAsync(id, session.TaiKhoanId);
        if (n == 0) throw new BusinessRuleException("Hóa đơn đã thu rồi, không thể thu lại (BR-11).");
        return new { ok = true, id };
    }

    /// <summary>
    /// Hóa đơn của khách thuê: server tự suy phòng từ token (khách không gửi phongId).
    /// Trả shape khớp khach_thue.js: danhSach/tongSo/trang/soLuongMoiTrang/phongId/soPhong/chiSoKyNay.
    /// </summary>
    public async Task<object> CuaToiAsync(SessionInfo session, JsonElement data)
    {
        var page = Math.Max(1, AccountService.GetOptionalInt(data, "page") ?? 1);
        var soLuong = Math.Clamp(AccountService.GetOptionalInt(data, "soLuongMoiTrang") ?? 10, 1, 100);

        var phongId = await PhongCuaKhachAsync(session.TaiKhoanId);
        if (phongId is null)
            return new { danhSach = Array.Empty<object>(), tongSo = 0, trang = page,
                soLuongMoiTrang = soLuong, phongId = (int?)null, soPhong = (string?)null,
                hopDong = (object?)null, chiSoKyNay = (object?)null };
        var phong = await _rooms.RoomByIdAsync(phongId.Value) ?? throw new NotFoundOrDeniedException();
        var soPhong = (string)phong["so_phong"]!;

        var tongSo = await _repo.CountByPhongAsync(phongId.Value);
        var rows = await _repo.ByPhongAsync(phongId.Value, page, soLuong);
        var danhSach = rows.Select(r => (object)new
        {
            id = Convert.ToInt32(r["id"]),
            kyCuoc = (string)r["ky_cuoc"]!,
            tienPhong = Convert.ToInt64(r["tien_phong"]),
            tienDien = Convert.ToInt64(r["tien_dien"]),
            tienNuoc = Convert.ToInt64(r["tien_nuoc"]),
            phiKhac = Convert.ToInt64(r["phi_khac"]),
            tongTien = Convert.ToInt64(r["tong_tien"]),
            trangThai = (string)r["trang_thai"]!,
            ngayDong = r["ngay_dong"] is null or DBNull
                ? null : Convert.ToDateTime(r["ngay_dong"]!).ToString("yyyy-MM-dd"),
            ghiChu = r["ghi_chu"] as string,
        }).ToArray();

        // chiSoKyNay = chỉ số kỳ mới nhất của phòng (UI vẽ phần "Kỳ này")
        var kyHienTai = rows.Count > 0 ? (string)rows[0]["ky_cuoc"]! : null;
        var cs = kyHienTai is null ? null : await _dienNuoc.ByKyAsync(phongId.Value, kyHienTai);
        var chiSoKyNay = cs is null ? null : new
        {
            kyCuoc = (string)cs["ky_cuoc"]!,
            dienCu = Convert.ToInt32(cs["dien_cu"]),
            dienMoi = Convert.ToInt32(cs["dien_moi"]),
            giaDien = Convert.ToInt64(cs["gia_dien"]),
            nuocCu = Convert.ToInt32(cs["nuoc_cu"]),
            nuocMoi = Convert.ToInt32(cs["nuoc_moi"]),
            giaNuoc = Convert.ToInt64(cs["gia_nuoc"]),
            hinhThucNuoc = (string)cs["hinh_thuc_nuoc"]!,
            soNguoiNuoc = cs["so_nguoi_nuoc"] is null or DBNull ? 0 : Convert.ToInt32(cs["so_nguoi_nuoc"]),
        };

        return new { danhSach, tongSo, trang = page, soLuongMoiTrang = soLuong,
            phongId = (int?)phongId.Value, soPhong, hopDong = (object?)null, chiSoKyNay };
    }

    /// <summary>
    /// DOI_SOAT_CHAY: quét giao dịch SePay (bảng giao_dich_chuyen_khoan nếu có, kỳ đối soát
    /// hiện chưa có dữ liệu nguồn — trả thống kê zeros khi token/api chưa cấu hình).
    /// Shape khớp cai_dat.js: soGiaoDichMoi / soKhop / soBoQua.
    /// </summary>
    public Task<object> DoiSoatAsync(SessionInfo session, JsonElement data)
    {
        // ponytail: chưa có nguồn giao dịch SePay hợp nhất (api_token cấu hình qua CAU_HINH_CAP_NHAT);
        // khi M4b cắm webhook SePay, đọc bảng giao dịch tại đây và khớp nội dung "<soPhong> <kyCuoc>".
        return Task.FromResult<object>(new { soGiaoDichMoi = 0L, soKhop = 0L, soBoQua = 0L });
    }

    private async Task<int?> PhongCuaKhachAsync(int taiKhoanId)
    {
        var rows = await _repo.Db.QueryAsync(
            "SELECT phong_id FROM ho_so_khach_thue WHERE tai_khoan_id = @id", new { id = taiKhoanId });
        var phongId = rows.SingleOrDefault()?["phong_id"];
        return phongId is null or DBNull ? null : Convert.ToInt32(phongId);
    }

    private static object HoaDonDto(Dictionary<string, object?> r) => new
    {
        id = Convert.ToInt32(r["id"]),
        phongId = Convert.ToInt32(r["phong_id"]!),
        soPhong = (string)r["so_phong"]!,
        kyCuoc = (string)r["ky_cuoc"]!,
        tienPhong = Convert.ToInt64(r["tien_phong"]),
        tienDien = Convert.ToInt64(r["tien_dien"]),
        tienNuoc = Convert.ToInt64(r["tien_nuoc"]),
        phiKhac = Convert.ToInt64(r["phi_khac"]),
        tongTien = Convert.ToInt64(r["tong_tien"]),
        trangThai = (string)r["trang_thai"]!,
        ngayDong = r["ngay_dong"] is null or DBNull
            ? null : Convert.ToDateTime(r["ngay_dong"]!).ToString("yyyy-MM-dd"),
        ghiChu = r["ghi_chu"] as string,
    };

    /// <summary>QuanLy chỉ được lập hóa đơn phòng thuộc khu của mình.</summary>
    private async Task RequirePhongScopeAsync(SessionInfo session, int phongId)
    {
        if (session.VaiTro != VaiTroNguoiDung.QuanLy) return;
        var phong = await _rooms.RoomByIdAsync(phongId) ?? throw new NotFoundOrDeniedException();
        if (session.KhuTroId != Convert.ToInt32(phong["khu_tro_id"]))
            throw new NotFoundOrDeniedException();
    }

    // M4a: tiền VND nguyên đồng (BIGINT) — GetOptionalInt tràn với giá lớn.
    private static long? GetOptionalLong(JsonElement data, string name)
    {
        if (data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty(name, out var el) &&
            el.ValueKind == JsonValueKind.Number &&
            el.TryGetInt64(out var v))
            return v;
        return null;
    }

    private static string LayKyCuoc(JsonElement data)
    {
        var ky = AccountService.GetString(data, "kyCuoc") ?? throw new ValidationException("Thiếu kỳ cước.");
        if (ky.Length != 7 || ky[4] != '-' ||
            !int.TryParse(ky.AsSpan(0, 4), out var nam) || nam < 2000 ||
            !int.TryParse(ky.AsSpan(5, 2), out var thang) || thang is < 1 or > 12)
            throw new ValidationException("Kỳ cước phải có định dạng yyyy-MM.");
        return ky;
    }
}
