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

    public async Task<int> UpdateProfileAsync(int taiKhoanId, string hoTen, string cccd, DateOnly ngaySinh,
        string soDienThoai, string queQuan, string? noiLamViec, bool? daDangKyTamTru, int? phongId, int nguoiThucHienId,
        string? passwordHash = null, int? khuTroScopeId = null)
    {
        var (conn, tx) = await _db.BeginAsync();
        try
        {
            await using (var scope = conn.CreateCommand())
            {
                scope.Transaction = tx;
                scope.CommandText = @"SELECT hs.phong_id, p.khu_tro_id
                    FROM ho_so_khach_thue hs LEFT JOIN phong p ON p.id=hs.phong_id
                    WHERE hs.tai_khoan_id=@id FOR UPDATE";
                scope.Parameters.AddWithValue("@id", taiKhoanId);
                await using var reader = await scope.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) throw new NotFoundOrDeniedException();
                var actualSite = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1);
                if (khuTroScopeId is not null && actualSite is not null && actualSite != khuTroScopeId)
                    throw new NotFoundOrDeniedException();
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE tai_khoan SET ho_ten=@n, ngay_cap_nhat=NOW() WHERE id=@id AND vai_tro='KhachThue'";
                cmd.Parameters.AddWithValue("@n", hoTen);
                cmd.Parameters.AddWithValue("@id", taiKhoanId);
                if (await cmd.ExecuteNonQueryAsync() == 0) throw new NotFoundOrDeniedException();
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"UPDATE ho_so_khach_thue SET cccd=@c, ngay_sinh=@ns, so_dien_thoai=@sdt,
                    que_quan=@q, noi_lam_viec=@nlv, da_dang_ky_tam_tru=COALESCE(@tamtru, da_dang_ky_tam_tru), ngay_cap_nhat=NOW()
                    WHERE tai_khoan_id=@id";
                cmd.Parameters.AddWithValue("@c", cccd);
                cmd.Parameters.AddWithValue("@ns", ngaySinh.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.AddWithValue("@sdt", soDienThoai);
                cmd.Parameters.AddWithValue("@q", queQuan);
                cmd.Parameters.AddWithValue("@nlv", (object?)noiLamViec ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@tamtru", daDangKyTamTru is null ? DBNull.Value : daDangKyTamTru.Value ? 1 : 0);
                cmd.Parameters.AddWithValue("@id", taiKhoanId);
                if (await cmd.ExecuteNonQueryAsync() == 0) throw new NotFoundOrDeniedException();
            }
            if (phongId is not null)
            {
                int? phongCu;
                int khuMoi;
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT phong_id FROM ho_so_khach_thue WHERE tai_khoan_id=@id FOR UPDATE";
                    cmd.Parameters.AddWithValue("@id", taiKhoanId);
                    var raw = await cmd.ExecuteScalarAsync();
                    phongCu = raw is null or DBNull ? null : Convert.ToInt32(raw);
                }
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT khu_tro_id, so_nguoi_toi_da FROM phong WHERE id=@id AND trang_thai<>'BaoTri' FOR UPDATE";
                    cmd.Parameters.AddWithValue("@id", phongId.Value);
                    await using var reader = await cmd.ExecuteReaderAsync();
                    if (!await reader.ReadAsync()) throw new NotFoundOrDeniedException();
                    khuMoi = reader.GetInt32(0);
                    var capacity = reader.GetInt32(1);
                    await reader.CloseAsync();
                    await using var count = conn.CreateCommand();
                    count.Transaction = tx;
                    count.CommandText = "SELECT COUNT(*) FROM ho_so_khach_thue WHERE phong_id=@p AND tai_khoan_id<>@id";
                    count.Parameters.AddWithValue("@p", phongId.Value);
                    count.Parameters.AddWithValue("@id", taiKhoanId);
                    if (Convert.ToInt32(await count.ExecuteScalarAsync()) >= capacity)
                        throw new BusinessRuleException("Phòng đã đầy, không thể gán thêm.");
                }
                if (phongCu != phongId)
                {
                    if (phongCu is not null)
                    {
                        await using (var release = conn.CreateCommand())
                        {
                            release.Transaction = tx;
                            release.CommandText = @"UPDATE phong SET trang_thai='Trong'
                                WHERE id=@p AND trang_thai='DaThue'
                                  AND NOT EXISTS (SELECT 1 FROM ho_so_khach_thue WHERE phong_id=@p AND tai_khoan_id<>@id)
                                  AND NOT EXISTS (SELECT 1 FROM hop_dong WHERE phong_id=@p AND trang_thai IN ('ChoNhanPhong','HieuLuc'))";
                            release.Parameters.AddWithValue("@p", phongCu.Value);
                            release.Parameters.AddWithValue("@id", taiKhoanId);
                            await release.ExecuteNonQueryAsync();
                        }
                        await using var oldHistory = conn.CreateCommand();
                        oldHistory.Transaction = tx;
                        oldHistory.CommandText = @"INSERT INTO lich_su_cu_tru (tai_khoan_khach_id, phong_id, khu_tro_id, loai_bien_dong, nguoi_thuc_hien_id)
                            SELECT @t, p.id, p.khu_tro_id, 'ChuyenDi', @n FROM phong p WHERE p.id=@p";
                        oldHistory.Parameters.AddWithValue("@t", taiKhoanId);
                        oldHistory.Parameters.AddWithValue("@p", phongCu.Value);
                        oldHistory.Parameters.AddWithValue("@n", nguoiThucHienId);
                        await oldHistory.ExecuteNonQueryAsync();
                    }
                    await using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "UPDATE ho_so_khach_thue SET phong_id=@p WHERE tai_khoan_id=@id";
                        cmd.Parameters.AddWithValue("@p", phongId.Value);
                        cmd.Parameters.AddWithValue("@id", taiKhoanId);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    await using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"UPDATE phong SET trang_thai='DaThue' WHERE id=@p;
                            INSERT INTO lich_su_cu_tru (tai_khoan_khach_id, phong_id, khu_tro_id, loai_bien_dong, nguoi_thuc_hien_id)
                            VALUES (@t, @p, @k, @loai, @n)";
                        cmd.Parameters.AddWithValue("@p", phongId.Value);
                        cmd.Parameters.AddWithValue("@t", taiKhoanId);
                        cmd.Parameters.AddWithValue("@k", khuMoi);
                        cmd.Parameters.AddWithValue("@loai", phongCu is null ? "NhanPhong" : "ChuyenDen");
                        cmd.Parameters.AddWithValue("@n", nguoiThucHienId);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            if (passwordHash is not null)
            {
                await using var password = conn.CreateCommand();
                password.Transaction = tx;
                password.CommandText = "UPDATE tai_khoan SET mat_khau_hash=@hash, ngay_cap_nhat=NOW() WHERE id=@id";
                password.Parameters.AddWithValue("@hash", passwordHash);
                password.Parameters.AddWithValue("@id", taiKhoanId);
                await password.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            return 1;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
        finally { await conn.CloseAsync(); }
    }

    /// <summary>Đăng ký public: tạo tài khoản + hồ sơ trong 1 transaction, nguyên tử.</summary>
    public async Task<int> RegisterAsync(
        string tenDangNhap, string matKhauHash, string hoTen,
        string cccd, DateOnly ngaySinh, string soDienThoai, string queQuan, string? noiLamViec,
        int? phongId = null, bool daDangKyTamTru = false, int? nguoiThucHienId = null)
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
                cmd.CommandText = @"INSERT INTO ho_so_khach_thue (tai_khoan_id, cccd, ngay_sinh, so_dien_thoai, que_quan, noi_lam_viec, phong_id, da_dang_ky_tam_tru)
                                    VALUES (@t, @c, @ns, @sdt, @q, @nlv, @p, @tamtru)";
                cmd.Parameters.AddWithValue("@t", tkId);
                cmd.Parameters.AddWithValue("@c", cccd);
                cmd.Parameters.AddWithValue("@ns", ngaySinh.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.AddWithValue("@sdt", soDienThoai);
                cmd.Parameters.AddWithValue("@q", queQuan);
                cmd.Parameters.AddWithValue("@nlv", (object?)noiLamViec ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@p", (object?)phongId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@tamtru", daDangKyTamTru ? 1 : 0);
                await cmd.ExecuteNonQueryAsync();
            }
            if (phongId is not null)
            {
                await using (var room = conn.CreateCommand())
                {
                    room.Transaction = tx;
                    room.CommandText = "SELECT khu_tro_id, so_nguoi_toi_da FROM phong WHERE id=@p AND trang_thai<>'BaoTri' FOR UPDATE";
                    room.Parameters.AddWithValue("@p", phongId.Value);
                    await using var reader = await room.ExecuteReaderAsync();
                    if (!await reader.ReadAsync()) throw new NotFoundOrDeniedException();
                    var khuTroId = reader.GetInt32(0);
                    var capacity = reader.GetInt32(1);
                    await reader.CloseAsync();
                    await using var count = conn.CreateCommand();
                    count.Transaction = tx;
                    count.CommandText = "SELECT COUNT(*) FROM ho_so_khach_thue WHERE phong_id=@p AND tai_khoan_id<>@t";
                    count.Parameters.AddWithValue("@p", phongId.Value);
                    count.Parameters.AddWithValue("@t", tkId);
                    if (Convert.ToInt32(await count.ExecuteScalarAsync()) >= capacity)
                        throw new BusinessRuleException("Phòng đã đầy, không thể gán thêm.");
                    await using var mark = conn.CreateCommand();
                    mark.Transaction = tx;
                    mark.CommandText = "UPDATE phong SET trang_thai='DaThue' WHERE id=@p";
                    mark.Parameters.AddWithValue("@p", phongId.Value);
                    await mark.ExecuteNonQueryAsync();
                    await using var history = conn.CreateCommand();
                    history.Transaction = tx;
                    history.CommandText = @"INSERT INTO lich_su_cu_tru (tai_khoan_khach_id, phong_id, khu_tro_id, loai_bien_dong, nguoi_thuc_hien_id)
                        VALUES (@t, @p, @k, 'NhanPhong', @n)";
                    history.Parameters.AddWithValue("@t", tkId);
                    history.Parameters.AddWithValue("@p", phongId.Value);
                    history.Parameters.AddWithValue("@k", khuTroId);
                    history.Parameters.AddWithValue("@n", (object?)nguoiThucHienId ?? DBNull.Value);
                    await history.ExecuteNonQueryAsync();
                }
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

    public Task<List<Dictionary<string, object?>>> SearchAsync(string keyword, int limit = 50, int? khuTroId = null)
    {
        var pattern = $"{keyword}%"; // escape trong service trước khi gọi
        return _db.QueryAsync(
            @"SELECT tk.id, tk.ho_ten, tk.ten_dang_nhap, hs.cccd, hs.ngay_sinh, hs.so_dien_thoai,
                     hs.que_quan, hs.noi_lam_viec, hs.phong_id, hs.da_dang_ky_tam_tru
              FROM ho_so_khach_thue hs
              JOIN tai_khoan tk ON tk.id = hs.tai_khoan_id
              LEFT JOIN phong p ON p.id=hs.phong_id
              WHERE (tk.ho_ten LIKE @kw ESCAPE '!' OR hs.cccd LIKE @kw ESCAPE '!' OR hs.so_dien_thoai LIKE @kw ESCAPE '!')
                AND (@k IS NULL OR hs.phong_id IS NULL OR p.khu_tro_id=@k)
              ORDER BY tk.ho_ten LIMIT @lim", new { kw = pattern, lim = limit, k = khuTroId });
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
            @"SELECT tk.id, tk.ten_dang_nhap, tk.ho_ten, hs.cccd, hs.ngay_sinh, hs.so_dien_thoai,
                     hs.que_quan, hs.noi_lam_viec, hs.phong_id, hs.da_dang_ky_tam_tru
              FROM ho_so_khach_thue hs JOIN tai_khoan tk ON tk.id = hs.tai_khoan_id
              WHERE (@p = 0 AND hs.phong_id IS NULL) OR hs.phong_id = @p ORDER BY tk.id", new { p = phongId });

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
            int phongCu, khuCu = 0, khuMoi;
            // khóa dòng hồ sơ của khách + khóa phòng đích FOR UPDATE
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT phong_id FROM ho_so_khach_thue WHERE tai_khoan_id = @t FOR UPDATE";
                cmd.Parameters.AddWithValue("@t", taiKhoanId);
                var raw = await cmd.ExecuteScalarAsync();
                phongCu = raw is DBNull or null ? 0 : Convert.ToInt32(raw);
            }
            if (phongCu != 0)
            {
                await using var source = conn.CreateCommand();
                source.Transaction = tx;
                source.CommandText = "SELECT khu_tro_id FROM phong WHERE id=@p FOR UPDATE";
                source.Parameters.AddWithValue("@p", phongCu);
                var raw = await source.ExecuteScalarAsync();
                khuCu = raw is DBNull or null ? 0 : Convert.ToInt32(raw);
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT khu_tro_id, so_nguoi_toi_da, trang_thai FROM phong WHERE id = @p FOR UPDATE";
                cmd.Parameters.AddWithValue("@p", phongIdMoi);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    throw new NotFoundOrDeniedException();
                khuMoi = reader.GetInt32(0);
                var sucChua = reader.GetInt32(1);
                var trangThaiMoi = reader.GetString(2);
                if (trangThaiMoi == "BaoTri") throw new BusinessRuleException("Phòng đang bảo trì, không thể chuyển vào.");
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
            if (phongCu != 0 && phongCu != phongIdMoi)
            {
                await using var release = conn.CreateCommand();
                release.Transaction = tx;
                release.CommandText = @"UPDATE phong SET trang_thai='Trong'
                    WHERE id=@p AND trang_thai='DaThue'
                      AND NOT EXISTS (SELECT 1 FROM ho_so_khach_thue WHERE phong_id=@p)
                      AND NOT EXISTS (SELECT 1 FROM hop_dong WHERE phong_id=@p AND trang_thai IN ('ChoNhanPhong','HieuLuc'))";
                release.Parameters.AddWithValue("@p", phongCu);
                await release.ExecuteNonQueryAsync();
            }
            await using (var occupy = conn.CreateCommand())
            {
                occupy.Transaction = tx;
                occupy.CommandText = "UPDATE phong SET trang_thai='DaThue' WHERE id=@p AND trang_thai='Trong'";
                occupy.Parameters.AddWithValue("@p", phongIdMoi);
                await occupy.ExecuteNonQueryAsync();
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
                cmd.CommandText = "SELECT khu_tro_id FROM phong WHERE id = @p FOR UPDATE";
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
            await using (var release = conn.CreateCommand())
            {
                release.Transaction = tx;
                release.CommandText = @"UPDATE phong SET trang_thai='Trong'
                    WHERE id=@p AND trang_thai='DaThue'
                      AND NOT EXISTS (SELECT 1 FROM ho_so_khach_thue WHERE phong_id=@p)
                      AND NOT EXISTS (SELECT 1 FROM hop_dong WHERE phong_id=@p AND trang_thai IN ('ChoNhanPhong','HieuLuc'))";
                release.Parameters.AddWithValue("@p", phongId);
                await release.ExecuteNonQueryAsync();
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
