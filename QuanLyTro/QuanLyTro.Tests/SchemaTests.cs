using QuanLyTro.Server.Data;
using QuanLyTro.Tests.TestHelpers;

namespace QuanLyTro.Tests;

[TestClass]
public class SchemaTests
{
    [TestMethod]
    public async Task InitializeTwice_DataPersists()
    {
        var db = TestDb.Create();
        await TestDb.ResetAsync();
        var init = new SchemaInitializer(db);
        await init.InitializeAsync();
        await db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro) VALUES ('u1','h','U 1','ChuTro')");
        await init.InitializeAsync(); // chạy lần 2 phải không mất dữ liệu
        var count = Convert.ToInt32(await db.ScalarAsync("SELECT COUNT(*) FROM tai_khoan"));
        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public async Task AllEightTables_Exist_WithConstraints()
    {
        var db = TestDb.Create();
        await TestDb.ResetAsync();
        await new SchemaInitializer(db).InitializeAsync();
        foreach (var table in new[] { "tai_khoan", "quyen_vai_tro", "quyen_mac_dinh_da_ap_dung",
                     "cau_hinh_he_thong", "khu_tro", "phong", "ho_so_khach_thue", "lich_su_cu_tru" })
        {
            var exists = await db.ScalarAsync(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = @db AND table_name = @t",
                new { db = TestDb.TestDatabaseName, t = table });
            Assert.AreEqual(1L, Convert.ToInt64(exists), $"thiếu bảng {table}");
        }
        // UNIQUE(khu_tro_id, so_phong) — statistics có 1 dòng/cột nên index 2 cột = 2 dòng
        var uk = await db.ScalarAsync(
            "SELECT COUNT(DISTINCT index_name) FROM information_schema.statistics WHERE table_schema=@db AND table_name='phong' AND index_name='UK_phong_khu_so'",
            new { db = TestDb.TestDatabaseName });
        Assert.AreEqual(1L, Convert.ToInt64(uk), "thiếu UNIQUE(khu_tro_id, so_phong)");
    }

    [TestMethod]
    public async Task CheckConstraints_EnforceValues()
    {
        var db = TestDb.Create();
        await TestDb.ResetAsync();
        await new SchemaInitializer(db).InitializeAsync();
        var khuId = Convert.ToInt32(await db.ExecuteAsync(
            "INSERT INTO khu_tro (ten_khu_tro, dia_chi) VALUES ('Khu A', 'Da Nang')"));
        // gia_thue <= 0 bị CHECK chặn
        await Assert.ThrowsExceptionAsync<MySqlConnector.MySqlException>(() =>
            db.ExecuteAsync("INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da, trang_thai) VALUES (@k, '101', 0, 3, 'Trong')",
                new { k = khuId }));
    }

    [TestMethod]
    public void WrongConnectionString_Fails()
    {
        Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => TestDb.EnsureTestDatabaseOnly("Server=127.0.0.1;Database=quanly_nhatro;Uid=root;"));
    }
}
