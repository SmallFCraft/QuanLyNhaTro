using System.Text.Json;
using QuanLyTro.Server.Network;
using QuanLyTro.Server.Repositories;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Models;
using QuanLyTro.Shared.Protocol;

namespace QuanLyTro.Server.Services;

/// <summary>
/// M3: nghiệp vụ hợp đồng thuê phòng + QR/PIN nhận phòng (BR-19).
/// ChuTro toàn quyền; QuanLy chỉ trong khu được gán; KhachThue chỉ nhận phòng qua QR/PIN.
/// </summary>
public sealed class HopDongService
{
    private readonly HopDongRepository _repo;
    private readonly RoomRepository _rooms;
    private readonly TenantRepository _tenants;

    public HopDongService(HopDongRepository repo, RoomRepository rooms, TenantRepository tenants)
    {
        _repo = repo;
        _rooms = rooms;
        _tenants = tenants;
    }

    public async Task<object> CreateAsync(SessionInfo session, JsonElement data)
    {
        if (session.VaiTro is not (VaiTroNguoiDung.ChuTro or VaiTroNguoiDung.QuanLy))
            throw new NotFoundOrDeniedException();
        var phongId = AccountService.GetInt(data, "phongId");
        var nguoiDaiDienId = AccountService.GetInt(data, "nguoiDaiDienId");
        var ngayBatDauStr = AccountService.GetString(data, "ngayBatDau") ?? throw new ValidationException("Thiếu ngày bắt đầu.");
        var ngayKetThucStr = AccountService.GetString(data, "ngayKetThuc") ?? throw new ValidationException("Thiếu ngày kết thúc.");
        var giaThue = AccountService.GetInt(data, "giaThue");
        var tienCoc = AccountService.GetOptionalInt(data, "tienCoc") ?? 0;
        var ghiChu = AccountService.GetString(data, "ghiChu");

        if (!DateOnly.TryParse(ngayBatDauStr, out var ngayBatDau))
            throw new ValidationException("Ngày bắt đầu không hợp lệ (định dạng yyyy-MM-dd).");
        if (!DateOnly.TryParse(ngayKetThucStr, out var ngayKetThuc))
            throw new ValidationException("Ngày kết thúc không hợp lệ (định dạng yyyy-MM-dd).");
        if (ngayKetThuc <= ngayBatDau)
            throw new ValidationException("Ngày kết thúc phải sau ngày bắt đầu.");
        if (giaThue <= 0)
            throw new ValidationException("Giá thuê phải lớn hơn 0.");
        if (tienCoc < 0)
            throw new ValidationException("Tiền cọc không được âm.");

        var phong = await _rooms.RoomByIdAsync(phongId) ?? throw new NotFoundOrDeniedException();
        if (session.VaiTro == VaiTroNguoiDung.QuanLy && session.KhuTroId != Convert.ToInt32(phong["khu_tro_id"]))
            throw new NotFoundOrDeniedException();
        // phòng phải đang trống mới lập được hợp đồng
        if ((string)phong["trang_thai"]! != "Trong")
            throw new BusinessRuleException("Phòng không còn trống, không thể lập hợp đồng mới.");

        // HOP_DONG_TAO luôn đi kèm mã QR/PIN (BR-19) — hợp đồng bắt đầu ở trạng thái chờ nhận phòng
        var hetHanMa = DateOnly.FromDateTime(DateTime.Today).AddDays(7);
        var (hopDongId, pin) = await _repo.TaoHopDongAsync(
            phongId, nguoiDaiDienId, ngayBatDau, ngayKetThuc,
            giaThue, tienCoc, "ChoNhanPhong", ghiChu, hetHanMa);
        return new { hopDongId, pin, qrData = $"QLT|{hopDongId}|{pin}" };
    }

    public async Task<object> SinhQrAsync(SessionInfo session, JsonElement data)
    {
        var hopDong = await LayTheoQuyenAsync(session, AccountService.GetInt(data, "hopDongId"));
        var tt = (string)hopDong["trang_thai"]!;
        if (tt != "ChoNhanPhong")
            throw new BusinessRuleException("Hợp đồng không còn ở trạng thái chờ nhận phòng, không sinh lại mã được.");
        // mã cũ chưa dùng bị vô hiệu, sinh mã mới thay thế
        await _repo.VoHieuMaAsync(Convert.ToInt32(hopDong["id"]));
        var pin = SinhPinMoi();
        var qrData = $"QLT|{Convert.ToInt32(hopDong["id"])}|{pin}";
        await _repo.ThemMaAsync(Convert.ToInt32(hopDong["id"]), pin, qrData);
        return new { hopDongId = Convert.ToInt32(hopDong["id"]), maPin = pin, qrDataString = qrData };
    }

    public async Task<object> NhanPhongQrAsync(SessionInfo session, JsonElement data)
    {
        if (session.VaiTro != VaiTroNguoiDung.KhachThue)
            throw new NotFoundOrDeniedException();
        var tokenOrPin = AccountService.GetString(data, "tokenOrPin") ?? throw new ValidationException("Thiếu mã nhận phòng.");
        tokenOrPin = tokenOrPin.Trim();
        if (tokenOrPin.Length == 0 || tokenOrPin.Length > 255)
            throw new ValidationException("Mã nhận phòng không hợp lệ.");

        var info = await _repo.NhanPhongAsync(tokenOrPin, session.TaiKhoanId)
            ?? throw new BusinessRuleException("Mã nhận phòng sai, đã hết hạn hoặc đã được sử dụng.");
        return new
        {
            hopDongId = Convert.ToInt32(info["hop_dong_id"]),
            soPhong = (string)info["so_phong"]!,
            ngayBatDau = Convert.ToDateTime(info["ngay_bat_dau"]).ToString("yyyy-MM-dd"),
        };
    }

    public async Task<object> GiaHanAsync(SessionInfo session, JsonElement data)
    {
        var hopDong = await LayTheoQuyenAsync(session, AccountService.GetInt(data, "hopDongId"));
        var newEndStr = AccountService.GetString(data, "newEndDate") ?? throw new ValidationException("Thiếu ngày kết thúc mới.");
        if (!DateOnly.TryParse(newEndStr, out var newEnd))
            throw new ValidationException("Ngày kết thúc mới không hợp lệ (định dạng yyyy-MM-dd).");
        var cur = DateOnly.FromDateTime(Convert.ToDateTime(hopDong["ngay_ket_thuc"]));
        if (newEnd <= cur)
            throw new ValidationException("Ngày kết thúc mới phải sau ngày kết thúc hiện tại.");
        if ((string)hopDong["trang_thai"]! != "HieuLuc")
            throw new BusinessRuleException("Chỉ gia hạn hợp đồng đang hiệu lực.");

        await _repo.GiaHanAsync(Convert.ToInt32(hopDong["id"]), newEnd);
        await _repo.ThemPhuLucAsync(Convert.ToInt32(hopDong["id"]), "GiaHan",
            $"Gia han den {newEnd:yyyy-MM-dd}", newEnd);
        return new { ok = true, ngayKetThuc = newEnd.ToString("yyyy-MM-dd") };
    }

    public async Task<object> ChamDutAsync(SessionInfo session, JsonElement data)
    {
        var hopDongId = AccountService.GetInt(data, "hopDongId");
        var hopDong = await LayTheoQuyenAsync(session, hopDongId);
        if ((string)hopDong["trang_thai"]! is "ChamDut")
            throw new BusinessRuleException("Hợp đồng đã chấm dứt trước đó.");
        var ghiChu = AccountService.GetString(data, "ghi_chu") ?? AccountService.GetString(data, "ghiChu");
        await _repo.ChamDutAsync(hopDongId, ghiChu);
        return new { ok = true };
    }

    public async Task<object> GetAllAsync(SessionInfo session, JsonElement data)
    {
        // QuanLy chỉ thấy hợp đồng phòng trong khu của mình — scope từ session, không từ payload
        var khu = session.VaiTro == VaiTroNguoiDung.QuanLy ? session.KhuTroId : null;
        var rows = await _repo.GetAllAsync(khu);
        return rows.Select(r => new
        {
            id = Convert.ToInt32(r["id"]),
            phongId = Convert.ToInt32(r["phong_id"]),
            nguoiDaiDienId = Convert.ToInt32(r["nguoi_dai_dien_id"]),
            tenNguoiDaiDien = (string)r["ten_nguoi_dai_dien"]!,
            soPhong = (string)r["so_phong"]!,
            ngayBatDau = Convert.ToDateTime(r["ngay_bat_dau"]).ToString("yyyy-MM-dd"),
            ngayKetThuc = Convert.ToDateTime(r["ngay_ket_thuc"]).ToString("yyyy-MM-dd"),
            giaThue = Convert.ToDecimal(r["gia_thue"]),
            tienCoc = Convert.ToDecimal(r["tien_coc"]),
            trangThai = (string)r["trang_thai"]!,
            ghiChu = r["ghi_chu"] as string,
        }).ToArray();
    }

    public async Task<object> PhuLucLayAsync(SessionInfo session, JsonElement data)
    {
        var hopDong = await LayTheoQuyenAsync(session, AccountService.GetInt(data, "hopDongId"));
        var hopDongId = Convert.ToInt32(hopDong["id"]);
        var phuLucs = await _repo.PhuLucByHopDongAsync(hopDongId);
        return new
        {
            hopDongId,
            soPhong = (string)hopDong["so_phong"]!,
            tenNguoiDaiDien = (string)hopDong["ten_nguoi_dai_dien"]!,
            cccdNguoiDaiDien = hopDong["cccd"] is string c ? c : null,
            dsPhuLuc = phuLucs.Select(r => new
            {
                id = Convert.ToInt32(r["id"]),
                loai = (string)r["loai"]!,
                noiDung = (string)r["noi_dung"]!,
                ngayApDung = r["ngay_ap_dung"] is null or DBNull ? null : Convert.ToDateTime(r["ngay_ap_dung"]).ToString("yyyy-MM-dd"),
                trangThai = (string)r["trang_thai"]!,
            }).ToArray(),
        };
    }

    public async Task<object> PhuLucCapNhatAsync(SessionInfo session, JsonElement data)
    {
        var hopDong = await LayTheoQuyenAsync(session, AccountService.GetInt(data, "hopDongId"));
        var hopDongId = Convert.ToInt32(hopDong["id"]);
        if (!data.TryGetProperty("nguoiOCung", out var ds) || ds.ValueKind != JsonValueKind.Array)
            throw new ValidationException("Thiếu danh sách người ở chung (nguoiOCung).");

        // mỗi người ở chung = 1 phụ lục ThemNguoi hiệu lực
        foreach (var n in ds.EnumerateArray())
        {
            var hoTen = n.TryGetProperty("hoTen", out var ht) && ht.ValueKind == JsonValueKind.String ? ht.GetString() : null;
            var cccd = n.TryGetProperty("cccd", out var cc) && cc.ValueKind == JsonValueKind.String ? cc.GetString() : null;
            var ngaySinhStr = n.TryGetProperty("ngaySinh", out var ns) && ns.ValueKind == JsonValueKind.String ? ns.GetString() : null;
            var queQuan = n.TryGetProperty("queQuan", out var qq) && qq.ValueKind == JsonValueKind.String ? qq.GetString() : null;
            var noiLamViec = n.TryGetProperty("noiLamViec", out var nlv) && nlv.ValueKind == JsonValueKind.String ? nlv.GetString() : null;
            var sdt = n.TryGetProperty("soDienThoai", out var sdtEl) && sdtEl.ValueKind == JsonValueKind.String ? sdtEl.GetString() : null;

            if (string.IsNullOrWhiteSpace(hoTen)) throw new ValidationException("Người ở chung thiếu họ tên.");
            if (cccd is null || cccd.Length != 12 || !cccd.All(char.IsAsciiDigit))
                throw new ValidationException($"CCCD của {hoTen} phải gồm đúng 12 chữ số.");
            if (ngaySinhStr is null || !DateOnly.TryParse(ngaySinhStr, out var ngaySinh))
                throw new ValidationException($"Ngày sinh của {hoTen} không hợp lệ.");
            if (ngaySinh >= DateOnly.FromDateTime(DateTime.Today))
                throw new ValidationException($"Ngày sinh của {hoTen} phải là ngày trong quá khứ.");

            await _repo.ThemPhuLucAsync(hopDongId, "ThemNguoi",
                $"Them nguoi o chung: {hoTen} | CCCD: {cccd} | NS: {ngaySinh:yyyy-MM-dd} | QQ: {queQuan ?? "-"} | NLV: {noiLamViec ?? "-"} | SDT: {sdt ?? "-"}",
                null);
        }
        return new { ok = true };
    }

    /// <summary>Hợp đồng phải tồn tại; QuanLy chỉ được chạm hợp đồng trong khu của mình.</summary>
    private async Task<Dictionary<string, object?>> LayTheoQuyenAsync(SessionInfo session, int hopDongId)
    {
        if (session.VaiTro is not (VaiTroNguoiDung.ChuTro or VaiTroNguoiDung.QuanLy))
            throw new NotFoundOrDeniedException();
        var hopDong = await _repo.ByIdAsync(hopDongId) ?? throw new NotFoundOrDeniedException();
        if (session.VaiTro == VaiTroNguoiDung.QuanLy && session.KhuTroId != Convert.ToInt32(hopDong["khu_tro_id"]))
            throw new NotFoundOrDeniedException();
        return hopDong;
    }

    private static string SinhPinMoi()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
        return new string(bytes.Select(b => (char)('0' + b % 10)).ToArray());
    }
}
