using QuanLyTro.Server.Network;
using QuanLyTro.Server.Repositories;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Models;
using QuanLyTro.Shared.Protocol;

using System.Text.Json;

namespace QuanLyTro.Server.Services;

/// <summary>
/// CRUD phòng theo scope khu. QuanLy chỉ thao tác trong khu của session;
/// tham số khuTroId ngoài scope trả cùng thông báo từ chối với bản ghi không tồn tại.
/// </summary>
public sealed class RoomService
{
    private readonly RoomRepository _repo;
    private readonly SiteRepository _siteRepo;

    public RoomService(RoomRepository repo, SiteRepository siteRepo)
    {
        _repo = repo;
        _siteRepo = siteRepo;
    }

    public async Task<object> ListAsync(SessionInfo session, JsonElement data)
    {
        // QuanLy: chỉ khu của session; ChuTro/LeTan/CongAn: thấy hết (hoặc lọc theo tham số nếu ChuTro)
        var requested = AccountService.GetOptionalInt(data, "khuTroId");
        var khuId = session.VaiTro == VaiTroNguoiDung.QuanLy
            ? session.KhuTroId
            : requested;
        var rows = await _repo.RoomsAsync(khuId);
        return rows.Select(RoomDto).ToArray();
    }

    public async Task<object?> GetDetailAsync(SessionInfo session, JsonElement data)
    {
        var id = AccountService.GetInt(data, "phongId");
        var room = await _repo.RoomByIdAsync(id);
        if (room is null) throw new NotFoundOrDeniedException();
        var khuId = Convert.ToInt32(room["khu_tro_id"]);
        if (session.VaiTro == VaiTroNguoiDung.QuanLy && session.KhuTroId != khuId)
            throw new NotFoundOrDeniedException(); // ngoài scope: cùng thông báo như không tồn tại
        return RoomDto(room);
    }

    public async Task<object> CreateAsync(SessionInfo session, JsonElement data)
    {
        var khuTroId = AccountService.GetInt(data, "khuTroId");
        var soPhong = AccountService.GetString(data, "soPhong") ?? throw new ValidationException("Thiếu số phòng.");
        var giaThue = GetDecimal(data, "giaThue");
        var soNguoiToiDa = AccountService.GetInt(data, "soNguoiToiDa");

        // Scope: QuanLy chỉ tạo trong khu được gán; khu không thuộc quyền → thông báo chung
        if (session.VaiTro == VaiTroNguoiDung.QuanLy)
        {
            if (session.KhuTroId is null || session.KhuTroId != khuTroId)
                throw new NotFoundOrDeniedException();
        }
        else if (session.VaiTro != VaiTroNguoiDung.ChuTro)
        {
            throw new NotFoundOrDeniedException(); // LeTan/CongAn/NhanVien/KhachThue không thêm phòng
        }

        if (giaThue <= 0) throw new ValidationException("Giá thuê phải lớn hơn 0.");
        if (soNguoiToiDa <= 0) throw new ValidationException("Số người tối đa phải lớn hơn 0.");
        if (string.IsNullOrWhiteSpace(soPhong)) throw new ValidationException("Số phòng không được rỗng.");

        var khuExists = await _siteRepo.CountRoomsAsync(khuTroId);
        var khuRow = await _siteRepo.ListAsync();
        if (!khuRow.Any(r => Convert.ToInt32(r["id"]) == khuTroId))
            throw new NotFoundOrDeniedException(); // khu không tồn tại hoặc ngoài quyền

        int id;
        try
        {
            id = await _repo.InsertPhongAsync(khuTroId, soPhong.Trim(), giaThue, soNguoiToiDa,
                AccountService.GetString(data, "moTa"));
        }
        catch (MySqlConnector.MySqlException ex) when (ex.Number == 1062)
        {
            throw new BusinessRuleException("Số phòng đã tồn tại trong khu này.");
        }
        return new { phongId = id };
    }

    public async Task<object> UpdateAsync(SessionInfo session, JsonElement data)
    {
        var id = AccountService.GetInt(data, "phongId");
        var room = await _repo.RoomByIdAsync(id) ?? throw new NotFoundOrDeniedException();
        var khuId = Convert.ToInt32(room["khu_tro_id"]);
        if (session.VaiTro == VaiTroNguoiDung.QuanLy && session.KhuTroId != khuId)
            throw new NotFoundOrDeniedException();
        if (session.VaiTro is not (VaiTroNguoiDung.ChuTro or VaiTroNguoiDung.QuanLy))
            throw new NotFoundOrDeniedException();

        var giaThue = GetDecimal(data, "giaThue");
        var soNguoiToiDa = AccountService.GetInt(data, "soNguoiToiDa");
        if (giaThue <= 0) throw new ValidationException("Giá thuê phải lớn hơn 0.");
        var soCuTru = await _repo.CountResidentsAsync(id);
        if (soNguoiToiDa < soCuTru)
            throw new BusinessRuleException($"Số người tối đa mới ({soNguoiToiDa}) nhỏ hơn số người đang ở ({soCuTru}).");

        var trangThai = AccountService.GetString(data, "trangThai");
        if (trangThai is not null && !new[] { "Trong", "DaThue", "BaoTri" }.Contains(trangThai))
            throw new ValidationException("Trạng thái phòng không hợp lệ.");

        var soPhong = AccountService.GetString(data, "soPhong");
        if (soPhong is not null && string.IsNullOrWhiteSpace(soPhong))
            throw new ValidationException("Số phòng không được rỗng.");
        try
        {
            await _repo.UpdatePhongAsync(id, soPhong?.Trim(), giaThue, soNguoiToiDa,
                AccountService.GetString(data, "moTa"), trangThai);
        }
        catch (MySqlConnector.MySqlException ex) when (ex.Number == 1062)
        {
            throw new BusinessRuleException("Số phòng đã tồn tại trong khu này.");
        }
        return new { ok = true };
    }

    public async Task<object> DeleteAsync(SessionInfo session, JsonElement data)
    {
        // PHONG_XOA chỉ ChuTro, khi 0 cư dân và 0 hợp đồng mở (QuanLy đi qua đề xuất — M4)
        if (session.VaiTro != VaiTroNguoiDung.ChuTro)
            throw new NotFoundOrDeniedException();
        var id = AccountService.GetInt(data, "phongId");
        var residents = await _repo.CountResidentsAsync(id);
        if (residents > 0)
            throw new BusinessRuleException("Phòng còn cư dân, không thể xóa.");
        var contracts = await _repo.CountOpenContractsAsync(id);
        if (contracts > 0)
            throw new BusinessRuleException("Phòng còn hợp đồng đang mở, không thể xóa.");
        var n = await _repo.DeletePhongAsync(id);
        if (n == 0) throw new NotFoundOrDeniedException();
        return new { ok = true };
    }

    public Task<int> CountResidentsAsync(int phongId) => _repo.CountResidentsAsync(phongId);

    private static decimal GetDecimal(JsonElement data, string name)
    {
        if (data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty(name, out var el) &&
            el.ValueKind == JsonValueKind.Number &&
            el.TryGetDecimal(out var v))
            return v;
        throw new ValidationException($"Thiếu hoặc sai kiểu trường {name}.");
    }

    private static object RoomDto(Dictionary<string, object?> r) => new
    {
        id = Convert.ToInt32(r["id"]),
        khuTroId = Convert.ToInt32(r["khu_tro_id"]),
        soPhong = (string)r["so_phong"]!,
        giaThue = Convert.ToDecimal(r["gia_thue"]),
        soNguoiToiDa = Convert.ToInt32(r["so_nguoi_toi_da"]),
        trangThai = (string)r["trang_thai"]!,
        moTa = r["mo_ta"] as string,
    };
}
