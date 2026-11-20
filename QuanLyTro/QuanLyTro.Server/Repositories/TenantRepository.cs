using MySqlConnector;
using QuanLyTro.Server.Data;
using QuanLyTro.Shared.Models;

namespace QuanLyTro.Server.Repositories;

public sealed class TenantRepository
{
    private readonly Database _db;
    public TenantRepository(Database db) => _db = db;
    public Database Db => _db;

    public Task<int> ScalarCountAsync(string sql, object param) => _db.ScalarT<int>(sql, param);

    public Task<int> UpdateHoTenAsync(int taiKhoanId, string hoTen) =>
        _db.ExecuteAsync(
            "UPDATE tai_khoan SET ho_ten = @n, ngay_cap_nhat = NOW() WHERE id = @id",
            new { n = hoTen, id = taiKhoanId });

    /// <summary>Đăng ký public: tạo tài khoản + hồ sơ trong 1 transaction, nguyên tử.</summary>
    public async Task<int> RegisterAsync(
        string tenDangNhap, string matKhauHash, string hoTen,
        string cccd, DateOnly ngaySinh, string soDienThoai, string queQuan, string? noiLamViec)
    {
        var (conn, tx) = await _db.BeginAsync();
        try
        {
            int tkId;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro)
                                    VALUES (@u, @h, @n, 'KhachThue')";
                cmd.Parameters.AddWithValue("@u", tenDangNhap);
                cmd.Parameters.AddWithValue("@h", matKhauHash);
                cmd.Parameters.AddWithValue("@n", hoTen);
                await cmd.ExecuteNonQueryAsync();
                tkId = (int)cmd.LastInsertedId;
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO ho_so_khach_thue (tai_khoan_id, cccd, ngay_sinh, so_dien_thoai, que_quan, noi_lam_viec)
                                    VALUES (@t, @c, @ns, @sdt, @q, @nlv)";
                cmd.Parameters.AddWithValue("@t", tkId);
                cmd.Parameters.AddWithValue("@c", cccd);
                cmd.Parameters.AddWithValue("@ns", ngaySinh.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.AddWithValue("@sdt", soDienThoai);
                cmd.Parameters.AddWithValue("@q", queQuan);
                cmd.Parameters.AddWithValue("@nlv", (object?)noiLamViec ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            return tkId;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public Task<List<Dictionary<string, object?>>> SearchAsync(string keyword, int limit = 50)
    {
        var pattern = $"{keyword}%"; // escape trong service trước khi gọi
        return _db.QueryAsync(
            @"SELECT tk.id, tk.ho_ten, tk.ten_dang_nhap, hs.cccd, hs.so_dien_thoai, hs.phong_id, hs.da_dang_ky_tam_tru
              FROM ho_so_khach_thue hs
              JOIN tai_khoan tk ON tk.id = hs.tai_khoan_id
              WHERE tk.ho_ten LIKE @kw ESCAPE '!' OR hs.cccd LIKE @kw ESCAPE '!' OR hs.so_dien_thoai LIKE @kw ESCAPE '!'
              ORDER BY tk.ho_ten LIMIT @lim", new { kw = pattern, lim = limit });
    }

    public Task<Dictionary<string, object?>?> ProfileAsync(int taiKhoanId) =>
        _db.QueryAsync(
            @"SELECT tk.id, tk.ho_ten, tk.ten_dang_nhap, hs.cccd, hs.ngay_sinh, hs.so_dien_thoai,
                     hs.que_quan, hs.noi_lam_viec, hs.phong_id, hs.da_dang_ky_tam_tru
              FROM ho_so_khach_thue hs JOIN tai_khoan tk ON tk.id = hs.tai_khoan_id
              WHERE tk.id = @id", new { id = taiKhoanId })
            .ContinueWith(t => (Dictionary<string, object?>?)t.Result.SingleOrDefault());

    public Task<List<Dictionary<string, object?>>> ByPhongAsync(int phongId) =>
        _db.QueryAsync(
            @"SELECT tk.id, tk.ho_ten, hs.cccd, hs.so_dien_thoai, hs.da_dang_ky_tam_tru
              FROM ho_so_khach_thue hs JOIN tai_khoan tk ON tk.id = hs.tai_khoan_id
              WHERE hs.phong_id = @p ORDER BY tk.id", new { p = phongId });

    public Task<int> UpdateLienHeAsync(int taiKhoanId, string? soDienThoai, string? noiLamViec) =>
        _db.ExecuteAsync(
            @"UPDATE ho_so_khach_thue SET so_dien_thoai = COALESCE(@sdt, so_dien_thoai),
                 noi_lam_viec = COALESCE(@nlv, noi_lam_viec), ngay_cap_nhat = NOW()
               WHERE tai_khoan_id = @id", new { sdt = soDienThoai, nlv = noiLamViec, id = taiKhoanId });

    public Task<int> UpdateTamTruAsync(int taiKhoanId, bool daDangKy) =>
        _db.ExecuteAsync(
            "UPDATE ho_so_khach_thue SET da_dang_ky_tam_tru = @d, ngay_cap_nhat = NOW() WHERE tai_khoan_id = @id",
            new { d = daDangKy ? 1 : 0, id = taiKhoanId });

    public Task<int> DeleteAsync(int taiKhoanId) =>
        // chỉ xóa khi phong_id IS NULL (điều kiện ở service); FK CASCADE xóa hồ sơ
        _db.ExecuteAsync(
            "DELETE FROM tai_khoan WHERE id = @id AND vai_tro = 'KhachThue'", new { id = taiKhoanId });
}

public static class TransactionHelper
{
    /// <summary>
    /// Chuyển phòng/ghi lịch sử nguyên tử trong 1 transaction với khóa dòng phòng đích.
    /// </summary>
    public static async Task MoveRoomAsync(Database db, TenantRepository repo,
        int taiKhoanId, int phongIdMoi, int khuTroIdMoi, int nguoiThucHienId)
    {
        var (conn, tx) = await db.BeginAsync();
        try
        {
            int phongCu, khuCu;
            // khóa dòng hồ sơ của khách + khóa phòng đích FOR UPDATE
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT phong_id FROM ho_so_khach_thue WHERE tai_khoan_id = @t FOR UPDATE";
                cmd.Parameters.AddWithValue("@t", taiKhoanId);
                var raw = await cmd.ExecuteScalarAsync();
                phongCu = raw is DBNull or null ? 0 : Convert.ToInt32(raw);
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT khu_tro_id, so_nguoi_toi_da FROM phong WHERE id = @p FOR UPDATE";
                cmd.Parameters.AddWithValue("@p", phongIdMoi);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    throw new NotFoundOrDeniedException();
                khuCu = reader.GetInt32(0);
                var sucChua = reader.GetInt32(1);
                await reader.CloseAsync();

                // sức chứa: người đang ở (không tính khách chuyển) + 1 phải <= sucChua
                await using (var cnt = conn.CreateCommand())
                {
                    cnt.Transaction = tx;
                    cnt.CommandText = @"SELECT COUNT(*) FROM ho_so_khach_thue
                                        WHERE phong_id = @p AND tai_khoan_id <> @t";
                    cnt.Parameters.AddWithValue("@p", phongIdMoi);
                    cnt.Parameters.AddWithValue("@t", taiKhoanId);
                    var dangO = Convert.ToInt32(await cnt.ExecuteScalarAsync());
                    if (dangO + 1 > sucChua)
                        throw new BusinessRuleException("Phòng đã đầy, không thể chuyển thêm.");
                }
            }

            if (phongCu != 0)
            {
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"INSERT INTO lich_su_cu_tru (tai_khoan_khach_id, phong_id, khu_tro_id, loai_bien_dong, nguoi_thuc_hien_id)
                                        VALUES (@t, @p, @k, 'ChuyenDi', @n)";
                    cmd.Parameters.AddWithValue("@t", taiKhoanId);
                    cmd.Parameters.AddWithValue("@p", phongCu);
                    cmd.Parameters.AddWithValue("@k", khuCu == 0 ? khuTroIdMoi : khuCu);
                    cmd.Parameters.AddWithValue("@n", nguoiThucHienId);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            await using (var upd = conn.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = "UPDATE ho_so_khach_thue SET phong_id = @p, ngay_cap_nhat = NOW() WHERE tai_khoan_id = @t";
                upd.Parameters.AddWithValue("@p", phongIdMoi);
                upd.Parameters.AddWithValue("@t", taiKhoanId);
                await upd.ExecuteNonQueryAsync();
            }
            await using (var den = conn.CreateCommand())
            {
                den.Transaction = tx;
                den.CommandText = @"INSERT INTO lich_su_cu_tru (tai_khoan_khach_id, phong_id, khu_tro_id, loai_bien_dong, nguoi_thuc_hien_id)
                                    VALUES (@t, @p, @k, 'ChuyenDen', @n)";
                den.Parameters.AddWithValue("@t", taiKhoanId);
                den.Parameters.AddWithValue("@p", phongIdMoi);
                den.Parameters.AddWithValue("@k", khuTroIdMoi);
                den.Parameters.AddWithValue("@n", nguoiThucHienId);
                await den.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    /// <summary>Trả phòng idempotent: chỉ ghi TraPhong khi phong_id đang khác NULL.</summary>
    public static async Task CheckOutAsync(Database db, int taiKhoanId, int nguoiThucHienId)
    {
        var (conn, tx) = await db.BeginAsync();
        try
        {
            int phongId, khuId;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT phong_id FROM ho_so_khach_thue WHERE tai_khoan_id = @t FOR UPDATE";
                cmd.Parameters.AddWithValue("@t", taiKhoanId);
                var raw = await cmd.ExecuteScalarAsync();
                if (raw is DBNull or null)
                {
                    await tx.CommitAsync(); // đã trả trước đó — idempotent
                    return;
                }
                phongId = Convert.ToInt32(raw);
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT khu_tro_id FROM phong WHERE id = @p";
                cmd.Parameters.AddWithValue("@p", phongId);
                var raw = await cmd.ExecuteScalarAsync();
                khuId = raw is DBNull or null ? 0 : Convert.ToInt32(raw);
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE ho_so_khach_thue SET phong_id = NULL, ngay_cap_nhat = NOW() WHERE tai_khoan_id = @t";
                cmd.Parameters.AddWithValue("@t", taiKhoanId);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO lich_su_cu_tru (tai_khoan_khach_id, phong_id, khu_tro_id, loai_bien_dong, nguoi_thuc_hien_id)
                                    VALUES (@t, @p, @k, 'TraPhong', @n)";
                cmd.Parameters.AddWithValue("@t", taiKhoanId);
                cmd.Parameters.AddWithValue("@p", phongId);
                cmd.Parameters.AddWithValue("@k", khuId);
                cmd.Parameters.AddWithValue("@n", nguoiThucHienId);
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
        finally
        {
            await conn.CloseAsync();
        }
    }
}
