using MySqlConnector;
using QuanLyTro.Server.Data;

namespace QuanLyTro.Server.Repositories;

/// <summary>
/// Truy vấn bảng de_xuat. Mọi query parameterized — không ghép chuỗi SQL.
/// </summary>
public sealed class DeXuatRepository
{
    private readonly Database _db;
    public DeXuatRepository(Database db) => _db = db;

    public Task<int> InsertAsync(string loai, string noiDung, int nguoiGuiId, int? khuTroId, int? doiTuongId = null, string? duLieu = null) =>
        _db.ExecuteAsync(
            @"INSERT INTO de_xuat (loai, noi_dung, nguoi_gui_id, khu_tro_id, doi_tuong_id, du_lieu)
              VALUES (@loai, @nd, @ng, @khu, @dt, @dl)",
            new { loai, nd = noiDung, ng = nguoiGuiId, khu = (object?)khuTroId ?? DBNull.Value, dt = (object?)doiTuongId ?? DBNull.Value, dl = (object?)duLieu ?? DBNull.Value });

    /// <summary>
    /// ChuTro: tất cả; QuanLy: khu mình + của chính mình; vai trò khác: của mình gửi.
    /// </summary>
    public Task<List<Dictionary<string, object?>>> LayTheoVaiTroAsync(
        string vaiTro, int taiKhoanId, int? khuTroId) =>
        vaiTro switch
        {
            "ChuTro" => _db.QueryAsync(
                @"SELECT dx.*, tk.ho_ten AS ten_nguoi_gui, khu.ten_khu_tro,
                         nd.ho_ten AS ten_nguoi_duyet
                  FROM de_xuat dx
                  JOIN tai_khoan tk ON tk.id = dx.nguoi_gui_id
                  LEFT JOIN khu_tro khu ON khu.id = dx.khu_tro_id
                  LEFT JOIN tai_khoan nd ON nd.id = dx.nguoi_duyet_id
                  ORDER BY dx.tao_luc DESC, dx.id DESC"),

            "QuanLy" => _db.QueryAsync(
                @"SELECT dx.*, tk.ho_ten AS ten_nguoi_gui, khu.ten_khu_tro,
                         nd.ho_ten AS ten_nguoi_duyet
                  FROM de_xuat dx
                  JOIN tai_khoan tk ON tk.id = dx.nguoi_gui_id
                  LEFT JOIN khu_tro khu ON khu.id = dx.khu_tro_id
                  LEFT JOIN tai_khoan nd ON nd.id = dx.nguoi_duyet_id
                  WHERE dx.nguoi_gui_id = @id OR dx.khu_tro_id = @khu
                  ORDER BY dx.tao_luc DESC, dx.id DESC",
                new { id = taiKhoanId, khu = (object?)khuTroId ?? DBNull.Value }),

            _ => _db.QueryAsync(
                @"SELECT dx.*, tk.ho_ten AS ten_nguoi_gui, khu.ten_khu_tro,
                         nd.ho_ten AS ten_nguoi_duyet
                  FROM de_xuat dx
                  JOIN tai_khoan tk ON tk.id = dx.nguoi_gui_id
                  LEFT JOIN khu_tro khu ON khu.id = dx.khu_tro_id
                  LEFT JOIN tai_khoan nd ON nd.id = dx.nguoi_duyet_id
                  WHERE dx.nguoi_gui_id = @id
                  ORDER BY dx.tao_luc DESC, dx.id DESC",
                new { id = taiKhoanId }),
        };

    public Task<Dictionary<string, object?>?> GetByIdAsync(int id) =>
        _db.QueryAsync("SELECT * FROM de_xuat WHERE id = @id", new { id })
           .ContinueWith(t => (Dictionary<string, object?>?)t.Result.SingleOrDefault());

    public Task<int> CapNhatTrangThaiAsync(int id, string trangThai, int nguoiDuyetId, string? phanHoi) =>
        _db.ExecuteAsync(
            @"UPDATE de_xuat SET trang_thai = @tt, nguoi_duyet_id = @nd,
              phan_hoi = @ph WHERE id = @id AND trang_thai = 'ChoDuyet'",
            new { tt = trangThai, nd = nguoiDuyetId, ph = (object?)phanHoi ?? DBNull.Value, id });

    /// <summary>Đặc tả MOD-04-07: khóa dòng đề xuất trong transaction dùng chung với nghiệp vụ gốc.</summary>
    public async Task<Dictionary<string, object?>?> GetByIdForUpdateAsync(MySqlConnection conn, MySqlTransaction tx, int id)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT * FROM de_xuat WHERE id = @id FOR UPDATE";
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);
        for (var i = 0; i < reader.FieldCount; i++)
            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        return row;
    }

    public async Task<int> CapNhatTrangThaiAsync(MySqlConnection conn, MySqlTransaction tx,
        int id, string trangThai, int nguoiDuyetId, string? phanHoi)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"UPDATE de_xuat SET trang_thai = @tt, nguoi_duyet_id = @nd,
              phan_hoi = @ph WHERE id = @id AND trang_thai = 'ChoDuyet'";
        cmd.Parameters.AddWithValue("@tt", trangThai);
        cmd.Parameters.AddWithValue("@nd", nguoiDuyetId);
        cmd.Parameters.AddWithValue("@ph", (object?)phanHoi ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync();
    }

    public Task<int> DemChoDuyetTrungAsync(string loai, int? doiTuongId) =>
        doiTuongId is null
            ? Task.FromResult(0)
            : _db.ScalarT<int>("SELECT COUNT(*) FROM de_xuat WHERE trang_thai='ChoDuyet' AND loai=@l AND doi_tuong_id=@dt",
                new { l = loai, dt = doiTuongId });

    public Task<int> DemChoDuyetAsync() =>
        _db.ScalarT<int>("SELECT COUNT(*) FROM de_xuat WHERE trang_thai = 'ChoDuyet'");
}
