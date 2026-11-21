using System.Text.Json;
using QuanLyTro.Server.Network;
using QuanLyTro.Server.Repositories;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Models;

namespace QuanLyTro.Server.Services;

/// <summary>
/// M4a: chốt chỉ số điện nước theo kỳ (BR-07). QuanLy chỉ thao tác phòng thuộc khu được gán;
/// ChuTro toàn quyền. Chỉ số mới < chỉ số cũ bị chặn ở service và CHECK constraint.
/// </summary>
public sealed class DienNuocService
{
    private readonly DienNuocRepository _repo;
    private readonly RoomRepository _rooms;

    public DienNuocService(DienNuocRepository repo, RoomRepository rooms)
    {
        _repo = repo;
        _rooms = rooms;
    }

    private static readonly System.Text.RegularExpressions.Regex KyCuocRegex =
        new(@"^\d{4}-\d{2}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public async Task<object> LayKyTruocAsync(SessionInfo session, JsonElement data)
    {
        var phongId = AccountService.GetInt(data, "phongId");
        var kyCuoc = LayKyCuoc(data);
        await RequirePhongScopeAsync(session, phongId);
        var prev = await _repo.LatestBeforeAsync(phongId, kyCuoc);
        if (prev is null)
            return new { phongId, kyCuoc, dienCu = 0, dienMoi = 0, nuocCu = 0, nuocMoi = 0,
                giaDien = 0L, giaNuoc = 0L, hinhThucNuoc = "Khoi", soNguoiNuoc = 0 };
        return new
        {
            phongId,
            kyCuoc = (string)prev["ky_cuoc"]!,
            dienCu = Convert.ToInt32(prev["dien_cu"]),
            dienMoi = Convert.ToInt32(prev["dien_moi"]),
            nuocCu = Convert.ToInt32(prev["nuoc_cu"]),
            nuocMoi = Convert.ToInt32(prev["nuoc_moi"]),
            giaDien = Convert.ToInt64(prev["gia_dien"]),
            giaNuoc = Convert.ToInt64(prev["gia_nuoc"]),
            hinhThucNuoc = (string)prev["hinh_thuc_nuoc"]!,
            soNguoiNuoc = prev["so_nguoi_nuoc"] is null or DBNull ? 0 : Convert.ToInt32(prev["so_nguoi_nuoc"]),
        };
    }

    public async Task<object> GhiSoAsync(SessionInfo session, JsonElement data)
    {
        var phongId = AccountService.GetInt(data, "phongId");
        var kyCuoc = LayKyCuoc(data);
        await RequirePhongScopeAsync(session, phongId);

        // UI gửi cả dienCu/nuocCu; thiếu thì lấy từ kỳ trước, chưa từng chốt thì 0.
        var prev = await _repo.LatestBeforeAsync(phongId, kyCuoc);
        var dienCu = AccountService.GetOptionalInt(data, "dienCu")
            ?? (prev is null ? 0 : Convert.ToInt32(prev["dien_moi"]));
        var nuocCu = AccountService.GetOptionalInt(data, "nuocCu")
            ?? (prev is null ? 0 : Convert.ToInt32(prev["nuoc_moi"]));
        var dienMoi = AccountService.GetInt(data, "dienMoi");
        var nuocMoi = AccountService.GetOptionalInt(data, "nuocMoi") ?? nuocCu;
        var giaDien = GetOptionalLong(data, "giaDien")
            ?? (prev is null ? 0L : Convert.ToInt64(prev["gia_dien"]));
        var giaNuoc = GetOptionalLong(data, "giaNuoc")
            ?? (prev is null ? 0L : Convert.ToInt64(prev["gia_nuoc"]));
        var hinhThucNuoc = AccountService.GetString(data, "hinhThucNuoc") ?? "Khoi";
        if (hinhThucNuoc is not ("Khoi" or "Nguoi" or "DauNguoi"))
            throw new ValidationException("Hình thức nước phải là Khoi, Nguoi hoặc DauNguoi.");
        var soNguoiNuoc = AccountService.GetOptionalInt(data, "soNguoiNuoc");

        if (dienMoi < dienCu)
            throw new ValidationException("Chỉ số điện mới phải lớn hơn hoặc bằng chỉ số cũ (BR-07).");
        if (nuocMoi < nuocCu)
            throw new ValidationException("Chỉ số nước mới phải lớn hơn hoặc bằng chỉ số cũ (BR-07).");
        if (hinhThucNuoc != "Khoi" && (soNguoiNuoc is null || soNguoiNuoc <= 0))
            throw new ValidationException("Nước theo đầu người cần số người > 0.");
        if (giaDien < 0 || giaNuoc < 0)
            throw new ValidationException("Giá điện/nước không được âm.");

        // hóa đơn đã lập là bất biến (BR-11) — chỉ số gốc của kỳ đã lập hóa đơn không được sửa
        if (await _repo.DemHoaDonByKyAsync(phongId, kyCuoc) > 0)
            throw new BusinessRuleException("Kỳ này đã có hóa đơn — không thể sửa chỉ số (hóa đơn là bất biến).");

        await _repo.UpsertAsync(phongId, kyCuoc, dienCu, dienMoi, giaDien, nuocCu, nuocMoi,
            giaNuoc, hinhThucNuoc, soNguoiNuoc);
        return new { ok = true };
    }

    /// <summary>QuanLy chỉ được chốt chỉ số phòng thuộc khu của mình.</summary>
    private async Task RequirePhongScopeAsync(SessionInfo session, int phongId)
    {
        if (session.VaiTro != VaiTroNguoiDung.QuanLy) return;
        var phong = await _rooms.RoomByIdAsync(phongId) ?? throw new NotFoundOrDeniedException();
        if (session.KhuTroId != Convert.ToInt32(phong["khu_tro_id"]))
            throw new NotFoundOrDeniedException();
    }

    private static string LayKyCuoc(JsonElement data)
    {
        var ky = AccountService.GetString(data, "kyCuoc") ?? throw new ValidationException("Thiếu kỳ cước.");
        if (!KyCuocRegex.IsMatch(ky))
            throw new ValidationException("Kỳ cước phải có định dạng yyyy-MM.");
        if (!int.TryParse(ky.AsSpan(5, 2), out var thang) || thang is < 1 or > 12)
            throw new ValidationException("Kỳ cước phải có định dạng yyyy-MM.");
        return ky;
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
}
