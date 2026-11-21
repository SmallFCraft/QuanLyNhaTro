using MySqlConnector;
using QuanLyTro.Server.Data;

namespace QuanLyTro.Server.Repositories;

/// <summary>M4a: đọc/ghi chỉ số điện nước theo (phòng, kỳ cước yyyy-MM).</summary>
public sealed class DienNuocRepository
{
    private readonly Database _db;

    public DienNuocRepository(Database db) => _db = db;

    public Database Db => _db;

    /// <summary>UPSERT chỉ số kỳ hiện tại (UNIQUE KEY phong_id + ky_cuoc).</summary>
    public async Task UpsertAsync(int phongId, string kyCuoc, int dienCu, int dienMoi,
        long giaDien, int nuocCu, int nuocMoi, long giaNuoc, string hinhThucNuoc, int? soNguoiNuoc)
    {
        var (conn, tx) = await _db.BeginAsync();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO chi_so_dien_nuoc
                (phong_id, ky_cuoc, dien_cu, dien_moi, nuoc_cu, nuoc_moi, gia_dien, gia_nuoc, hinh_thuc_nuoc, so_nguoi_nuoc)
                VALUES (@p, @ky, @dc, @dm, @nc, @nm, @gd, @gn, @htn, @ng)
                ON DUPLICATE KEY UPDATE
                    dien_cu = VALUES(dien_cu), dien_moi = VALUES(dien_moi),
                    nuoc_cu = VALUES(nuoc_cu), nuoc_moi = VALUES(nuoc_moi),
                    gia_dien = VALUES(gia_dien), gia_nuoc = VALUES(gia_nuoc),
                    hinh_thuc_nuoc = VALUES(hinh_thuc_nuoc), so_nguoi_nuoc = VALUES(so_nguoi_nuoc),
                    ngay_ghi = NOW()";
            cmd.Parameters.AddWithValue("@p", phongId);
            cmd.Parameters.AddWithValue("@ky", kyCuoc);
            cmd.Parameters.AddWithValue("@dc", dienCu);
            cmd.Parameters.AddWithValue("@dm", dienMoi);
            cmd.Parameters.AddWithValue("@nc", nuocCu);
            cmd.Parameters.AddWithValue("@nm", nuocMoi);
            cmd.Parameters.AddWithValue("@gd", giaDien);
            cmd.Parameters.AddWithValue("@gn", giaNuoc);
            cmd.Parameters.AddWithValue("@htn", hinhThucNuoc);
            cmd.Parameters.AddWithValue("@ng", (object?)soNguoiNuoc ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
            tx.Commit();
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

    /// <summary>Chỉ số kỳ trước của phòng: dien_moi/nuoc_moi kỳ đó làm dien_cu/nuoc_cu kỳ này.</summary>
    public Task<Dictionary<string, object?>?> LatestBeforeAsync(int phongId, string kyCuoc) =>
        _db.QueryAsync(
            @"SELECT phong_id, ky_cuoc, dien_cu, dien_moi, nuoc_cu, nuoc_moi, gia_dien, gia_nuoc,
                     hinh_thuc_nuoc, so_nguoi_nuoc
              FROM chi_so_dien_nuoc
              WHERE phong_id = @p AND ky_cuoc < @ky
              ORDER BY ky_cuoc DESC
              LIMIT 1", new { p = phongId, ky = kyCuoc })
            .ContinueWith(t => (Dictionary<string, object?>?)t.Result.SingleOrDefault());

    public Task<Dictionary<string, object?>?> ByKyAsync(int phongId, string kyCuoc) =>
        _db.QueryAsync(
            @"SELECT phong_id, ky_cuoc, dien_cu, dien_moi, nuoc_cu, nuoc_moi, gia_dien, gia_nuoc,
                     hinh_thuc_nuoc, so_nguoi_nuoc
              FROM chi_so_dien_nuoc
              WHERE phong_id = @p AND ky_cuoc = @ky", new { p = phongId, ky = kyCuoc })
            .ContinueWith(t => (Dictionary<string, object?>?)t.Result.SingleOrDefault());
}
