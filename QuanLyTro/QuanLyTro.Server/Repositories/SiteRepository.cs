using MySqlConnector;
using QuanLyTro.Server.Data;

namespace QuanLyTro.Server.Repositories;

public sealed class SiteRepository
{
    private readonly Database _db;
    public SiteRepository(Database db) => _db = db;

    public Task<List<Dictionary<string, object?>>> ListAsync() =>
        _db.QueryAsync("SELECT * FROM khu_tro ORDER BY id");

    public Task<int> CountRoomsAsync(int khuTroId) =>
        _db.ScalarT<int>("SELECT COUNT(*) FROM phong WHERE khu_tro_id = @k", new { k = khuTroId });

    public Task<int> InsertKhuAsync(string ten, string diaChi, int? qlId) =>
        _db.ExecuteAsync(
            "INSERT INTO khu_tro (ten_khu_tro, dia_chi, quan_ly_tai_khoan_id) VALUES (@t, @d, @q)",
            new { t = ten, d = diaChi, q = qlId });

    public Task<int> UpdateKhuAsync(int id, string ten, string diaChi) =>
        _db.ExecuteAsync(
            "UPDATE khu_tro SET ten_khu_tro = @t, dia_chi = @d, ngay_cap_nhat = NOW() WHERE id = @id",
            new { t = ten, d = diaChi, id });

    public Task<int> SuspendAsync(int id) =>
        _db.ExecuteAsync(
            "UPDATE khu_tro SET bi_ngung_hoat_dong = 1, ngay_cap_nhat = NOW() WHERE id = @id", new { id });

    public Task<int> DeleteKhuAsync(int id) =>
        _db.ExecuteAsync("DELETE FROM khu_tro WHERE id = @id", new { id });

    public Task<int> AssignManagerAsync(int khuTroId, int quanLyTaiKhoanId) =>
        _db.ExecuteAsync(
            "UPDATE khu_tro SET quan_ly_tai_khoan_id = @q, ngay_cap_nhat = NOW() WHERE id = @id",
            new { q = quanLyTaiKhoanId, id = khuTroId });

    public Task<int> ClearManagerOfAsync(int quanLyTaiKhoanId) =>
        _db.ExecuteAsync(
            "UPDATE khu_tro SET quan_ly_tai_khoan_id = NULL WHERE quan_ly_tai_khoan_id = @q", new { q = quanLyTaiKhoanId });

    /// <summary>Đồng bộ tai_khoan.khu_tro_id cho tài khoản QuanLy (session mới sẽ mang scope đúng).</summary>
    public Task<int> SetAccountKhuTroAsync(int taiKhoanId, int khuTroId) =>
        _db.ExecuteAsync(
            "UPDATE tai_khoan SET khu_tro_id = @k, ngay_cap_nhat = NOW() WHERE id = @id",
            new { k = khuTroId, id = taiKhoanId });

    public Task<int?> TryQuanLyAccountIdByTenDangNhapAsync(string tenDangNhap) =>
        _db.ScalarT<int?>(
            "SELECT id FROM tai_khoan WHERE ten_dang_nhap = @u AND vai_tro = 'QuanLy'",
            new { u = tenDangNhap });
}
