using QuanLyTro.Server.Data;

namespace QuanLyTro.Server.Repositories;

public sealed class RoomRepository
{
    private readonly Database _db;
    public RoomRepository(Database db) => _db = db;

    public Task<List<Dictionary<string, object?>>> RoomsAsync(int? khuTroId) =>
        khuTroId is null
            ? _db.QueryAsync("SELECT * FROM phong ORDER BY khu_tro_id, so_phong")
            : _db.QueryAsync("SELECT * FROM phong WHERE khu_tro_id = @k ORDER BY so_phong", new { k = khuTroId });

    public Task<Dictionary<string, object?>?> RoomByIdAsync(int id) =>
        _db.QueryAsync("SELECT * FROM phong WHERE id = @id", new { id })
            .ContinueWith(t => (Dictionary<string, object?>?)t.Result.SingleOrDefault());

    public Task<int> CountResidentsAsync(int phongId) =>
        _db.ScalarT<int>("SELECT COUNT(*) FROM ho_so_khach_thue WHERE phong_id = @p", new { p = phongId });

    public Task<int> CountOpenContractsAsync(int phongId) =>
        _db.ScalarT<int>(
            "SELECT COUNT(*) FROM hop_dong WHERE phong_id = @p AND trang_thai IN ('ChoNhanPhong','HieuLuc')",
            new { p = phongId });

    public Task<int> InsertPhongAsync(int khuTroId, string soPhong, decimal giaThue, int soNguoiToiDa, string? moTa) =>
        _db.ExecuteAsync(
            @"INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da, mo_ta)
              VALUES (@k, @s, @g, @n, @m)",
            new { k = khuTroId, s = soPhong, g = giaThue, n = soNguoiToiDa, m = moTa });

    public Task<int> UpdatePhongAsync(int id, string? soPhong, decimal giaThue, int soNguoiToiDa, string? moTa, string? trangThai) =>
        _db.ExecuteAsync(
            @"UPDATE phong SET so_phong = COALESCE(@s, so_phong), gia_thue = @g, so_nguoi_toi_da = @n,
                 mo_ta = COALESCE(@m, mo_ta),
                 trang_thai = COALESCE(@tt, trang_thai),
                 ngay_cap_nhat = NOW()
               WHERE id = @id",
            new { s = soPhong, g = giaThue, n = soNguoiToiDa, m = moTa, tt = trangThai, id });

    public Task<int> DeletePhongAsync(int id) =>
        _db.ExecuteAsync("DELETE FROM phong WHERE id = @id", new { id });
}
