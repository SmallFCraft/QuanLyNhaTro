using QuanLyTro.Server.Data;

namespace QuanLyTro.Server.Repositories;

/// <summary>
/// Truy vấn bảng su_co. Sắp xếp: KhanCap trước, rồi theo tao_luc giảm dần.
/// </summary>
public sealed class SuCoRepository
{
    private readonly Database _db;
    public SuCoRepository(Database db) => _db = db;

    private const string OrderBy =
        @"ORDER BY FIELD(sc.muc_do,'KhanCap','Cao','TrungBinh','Thap'), sc.tao_luc DESC, sc.id DESC";

    public Task<int> InsertAsync(int phongId, int nguoiBaoCaoId, string mucDo, string moTa) =>
        _db.ExecuteAsync(
            @"INSERT INTO su_co (phong_id, nguoi_bao_cao_id, muc_do, mo_ta)
              VALUES (@p, @nb, @md, @mt)",
            new { p = phongId, nb = nguoiBaoCaoId, md = mucDo, mt = moTa });

    /// <summary>ChuTro: tất cả; QuanLy: lọc theo khu (qua phong.khu_tro_id); null: tất cả.</summary>
    public Task<List<Dictionary<string, object?>>> LayTheoKhuAsync(int? khuTroId) =>
        khuTroId.HasValue
            ? _db.QueryAsync(
                $@"SELECT sc.*, p.so_phong, p.khu_tro_id, khu.ten_khu_tro,
                          tk.ho_ten AS ten_nguoi_bao
                   FROM su_co sc
                   JOIN phong p ON p.id = sc.phong_id
                   JOIN khu_tro khu ON khu.id = p.khu_tro_id
                   JOIN tai_khoan tk ON tk.id = sc.nguoi_bao_cao_id
                   WHERE p.khu_tro_id = @khu
                   {OrderBy}",
                new { khu = khuTroId.Value })
            : _db.QueryAsync(
                $@"SELECT sc.*, p.so_phong, p.khu_tro_id, khu.ten_khu_tro,
                          tk.ho_ten AS ten_nguoi_bao
                   FROM su_co sc
                   JOIN phong p ON p.id = sc.phong_id
                   JOIN khu_tro khu ON khu.id = p.khu_tro_id
                   JOIN tai_khoan tk ON tk.id = sc.nguoi_bao_cao_id
                   {OrderBy}");

    public Task<Dictionary<string, object?>?> GetByIdAsync(int id) =>
        _db.QueryAsync(
                @"SELECT sc.*, p.khu_tro_id, p.so_phong
                  FROM su_co sc JOIN phong p ON p.id = sc.phong_id
                  WHERE sc.id = @id",
                new { id })
           .ContinueWith(t => (Dictionary<string, object?>?)t.Result.SingleOrDefault());

    public Task<int> CapNhatTrangThaiAsync(int id, string trangThai, string? ketQua)
    {
        var xuLyXong = trangThai == "DaXuLy" ? "NOW()" : "xu_ly_xong_luc";
        return _db.ExecuteAsync(
            $@"UPDATE su_co SET trang_thai = @tt, ket_qua = COALESCE(@kq, ket_qua),
               xu_ly_xong_luc = {xuLyXong}
               WHERE id = @id",
            new { tt = trangThai, kq = (object?)ketQua ?? DBNull.Value, id });
    }

    public Task<int?> GetPhongIdAsync(int suCoId) =>
        _db.ScalarT<int?>("SELECT phong_id FROM su_co WHERE id = @id", new { id = suCoId });
}
