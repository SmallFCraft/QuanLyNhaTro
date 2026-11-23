using System.Text.Json;
using QuanLyTro.Server.Data;
using QuanLyTro.Server.Network;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Models;
using QuanLyTro.Shared.Protocol;
using QuanLyTro.Tests.TestHelpers;

namespace QuanLyTro.Tests;

[TestClass]
public class DeXuatSuCoTests
{
    private Database _db = null!;
    private SessionStore _sessions = null!;
    private RequestRouter _router = null!;

    private string _chuTroToken = null!;
    private string _quanLyToken = null!;
    private string _khachToken = null!;

    private int _chuTroId;
    private int _khuId;
    private int _khuB_Id;
    private int _phong1Id;
    private int _phong2Id;
    private int _khachId;

    [TestInitialize]
    public async Task Setup()
    {
        await TestDb.ResetAsync();
        _db = TestDb.Create();
        await new SchemaInitializer(_db).InitializeAsync();
        _sessions = new SessionStore();
        var auth = new AuthenticationService(_db, _sessions);
        _router = new RequestRouter(_db, _sessions);

        // Tao tai khoan ChuTro
        _chuTroId = await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro) VALUES ('chutro', @h, 'Chu Tro', 'ChuTro')",
            new { h = PasswordHasher.Hash("chutro") });
        _chuTroToken = (await auth.LoginAsync("chutro", "chutro")).Token;

        // Tao khu A, B
        _khuId = await _db.ExecuteAsync(
            "INSERT INTO khu_tro (ten_khu_tro, dia_chi) VALUES ('Khu A', 'Da Nang')");
        _khuB_Id = await _db.ExecuteAsync(
            "INSERT INTO khu_tro (ten_khu_tro, dia_chi) VALUES ('Khu B', 'Da Nang')");

        // Tao QuanLy khu A
        await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro, khu_tro_id) VALUES ('quanly', @h, 'Quan Ly A', 'QuanLy', @k)",
            new { h = PasswordHasher.Hash("quanly"), k = _khuId });
        _quanLyToken = (await auth.LoginAsync("quanly", "quanly")).Token;

        // Tao 2 phong khu A + KhachThue o phong 101
        _phong1Id = await _db.ExecuteAsync(
            "INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da, trang_thai) VALUES (@k, '101', 2500000, 2, 'DaThue')",
            new { k = _khuId });
        _phong2Id = await _db.ExecuteAsync(
            "INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da, trang_thai) VALUES (@k, '102', 2200000, 2, 'Trong')",
            new { k = _khuId });

        _khachId = await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro) VALUES ('khach01', @h, 'Khach Thu 01', 'KhachThue')",
            new { h = PasswordHasher.Hash("mk123456") });
        await _db.ExecuteAsync(
            "INSERT INTO ho_so_khach_thue (tai_khoan_id, cccd, ngay_sinh, so_dien_thoai, que_quan, phong_id) VALUES (@t, '012345678901', '2000-01-01', '0901234567', 'Quang Nam', @p)",
            new { t = _khachId, p = _phong1Id });
        _khachToken = (await auth.LoginAsync("khach01", "mk123456")).Token;
    }

    private async Task<ResponsePacket> Call(string action, string? token, object? data) =>
        await _router.HandleAsync(RequestPacket.Create(action, token, data), new ClientContext("test"));

    private static JsonElement ToJson(ResponsePacket resp) =>
        JsonDefaults.Deserialize<JsonElement>(JsonDefaults.Serialize(resp.Data!));

    private static int GetInt(ResponsePacket resp, string prop) =>
        ToJson(resp).GetProperty(prop).GetInt32();

    // ── DE XUAT ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task DeXuat_QuanLyGui_ChuTroDuyet_TrangThaiDung()
    {
        // QuanLy gui de xuat (thu gia mao khuTroId khac -> server phai lay tu session)
        var r1 = await Call(ActionNames.DeXuatTao, _quanLyToken, new
        {
            loai = "MuaSam",
            noiDung = "Can mua them binh chua chay",
            khuTroId = _khuB_Id
        });
        Assert.IsTrue(r1.Success, "TAO: " + r1.Message);
        var dxId = GetInt(r1, "deXuatId");

        // ChuTro duyet
        var r2 = await Call(ActionNames.DeXuatDuyet, _chuTroToken, new
        {
            deXuatId = dxId,
            phanHoi = "Dong y, mua ngay"
        });
        Assert.IsTrue(r2.Success, "DUYET: " + r2.Message);

        // Kiem tra DB: trang_thai, nguoi_duyet_id chinh xac = ChuTro, khu_tro_id = khu A
        var row = await _db.QueryAsync(
            "SELECT trang_thai, nguoi_duyet_id, phan_hoi, khu_tro_id FROM de_xuat WHERE id = @id",
            new { id = dxId });
        Assert.AreEqual(1, row.Count);
        Assert.AreEqual("DaDuyet", row[0]["trang_thai"]);
        Assert.AreEqual(_chuTroId, Convert.ToInt32(row[0]["nguoi_duyet_id"]));
        Assert.AreEqual("Dong y, mua ngay", row[0]["phan_hoi"]);
        Assert.AreEqual(_khuId, Convert.ToInt32(row[0]["khu_tro_id"]));

        // QuanLy xem lai phai thay phanHoi
        var list = await Call(ActionNames.DeXuatLayTatCa, _quanLyToken, new { });
        Assert.IsTrue(list.Success);
        var arr = ToJson(list);
        Assert.AreEqual(1, arr.GetArrayLength());
        Assert.AreEqual("Dong y, mua ngay", arr[0].GetProperty("phanHoi").GetString());
    }

    [TestMethod]
    public async Task DeXuat_QuanLyGoi_BaoCaoTongQuan_BiTuChoi()
    {
        var r = await Call(ActionNames.BaoCaoTongQuan, _quanLyToken, new { });
        Assert.IsFalse(r.Success, "QuanLy khong duoc phep goi BAO_CAO_TONG_QUAN");
    }

    [TestMethod]
    public async Task DeXuat_ChuTroGui_BiTuChoi()
    {
        var r = await Call(ActionNames.DeXuatTao, _chuTroToken, new
        {
            loai = "Khac",
            noiDung = "Chu tro khong tu gui de xuat"
        });
        Assert.IsFalse(r.Success, "ChuTro khong duoc phep gui de xuat");
    }

    [TestMethod]
    public async Task DeXuat_Badge_DemChoDuyetDung()
    {
        var r1 = await Call(ActionNames.DeXuatTao, _quanLyToken, new { loai = "Khac", noiDung = "Yeu cau 1" });
        await Call(ActionNames.DeXuatTao, _quanLyToken, new { loai = "MuaSam", noiDung = "Yeu cau 2" });
        var dx1 = GetInt(r1, "deXuatId");

        // ChuTro xem: soChoDuyet = 2
        var list1 = await Call(ActionNames.DeXuatLayTatCa, _chuTroToken, new { });
        Assert.IsTrue(list1.Success);
        var arr1 = ToJson(list1);
        Assert.AreEqual(2, arr1.GetArrayLength());
        Assert.AreEqual(2, arr1[0].GetProperty("soChoDuyet").GetInt32());

        // Duyet 1 cai -> soChoDuyet giam con 1
        var approve = await Call(ActionNames.DeXuatDuyet, _chuTroToken, new { deXuatId = dx1, phanHoi = "OK" });
        Assert.IsTrue(approve.Success);

        var list2 = await Call(ActionNames.DeXuatLayTatCa, _chuTroToken, new { });
        Assert.IsTrue(list2.Success);
        var arr2 = ToJson(list2);
        Assert.AreEqual(1, arr2[0].GetProperty("soChoDuyet").GetInt32());
    }

    [TestMethod]
    public async Task DeXuat_TuChoi_TrangThaiTuChoi_KhongDuyetLaiDuoc()
    {
        var r1 = await Call(ActionNames.DeXuatTao, _quanLyToken, new { loai = "Khac", noiDung = "Test tu choi" });
        Assert.IsTrue(r1.Success);
        var dxId = GetInt(r1, "deXuatId");

        var r2 = await Call(ActionNames.DeXuatTuChoi, _chuTroToken, new
        {
            deXuatId = dxId,
            phanHoi = "Khong duyet"
        });
        Assert.IsTrue(r2.Success, "TU_CHOI: " + r2.Message);

        var row = await _db.QueryAsync("SELECT trang_thai, nguoi_duyet_id, phan_hoi FROM de_xuat WHERE id = @id", new { id = dxId });
        Assert.AreEqual("TuChoi", row[0]["trang_thai"]);
        Assert.AreEqual(_chuTroId, Convert.ToInt32(row[0]["nguoi_duyet_id"]));
        Assert.AreEqual("Khong duyet", row[0]["phan_hoi"]);

        // Khong the duyet lai de xuat da xu ly
        var r3 = await Call(ActionNames.DeXuatDuyet, _chuTroToken, new { deXuatId = dxId });
        Assert.IsFalse(r3.Success);
    }

    // ── SU CO ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SuCo_KhachThue_BaoPhongMinh_ThanhCong()
    {
        var r = await Call(ActionNames.SuCoTao, _khachToken, new
        {
            mucDo = "Cao",
            moTa = "Nuoc bi ro ri tu tran"
        });
        Assert.IsTrue(r.Success, "BAO SU CO: " + r.Message);
        var scId = GetInt(r, "suCoId");

        // Phai co trong danh sach khu
        var list = await Call(ActionNames.SuCoLayTheoKhu, _quanLyToken, new { });
        Assert.IsTrue(list.Success);
        var arr = ToJson(list);
        var found = false;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.GetProperty("id").GetInt32() == scId)
            {
                Assert.AreEqual(_phong1Id, item.GetProperty("phongId").GetInt32());
                found = true;
                break;
            }
        }
        Assert.IsTrue(found, "Su co phai xuat hien trong danh sach khu");
    }

    [TestMethod]
    public async Task SuCo_KhachThue_BaoPhongNguoiKhac_BiTuChoi()
    {
        // Khach01 dang o phong1Id, thu gia mao bao su co cho phong2Id -> phai bi tu choi
        var r = await Call(ActionNames.SuCoTao, _khachToken, new
        {
            phongId = _phong2Id,
            mucDo = "Cao",
            moTa = "Thu bao su co phong nguoi khac"
        });
        Assert.IsFalse(r.Success, "KhachThue khong duoc bao su co phong khong phai cua minh");
    }

    [TestMethod]
    public async Task SuCo_KhachThue_ChuaCoPhong_BiTuChoi()
    {
        var khach2Id = await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro) VALUES ('khach02', @h, 'Khach 02', 'KhachThue')",
            new { h = PasswordHasher.Hash("mk123456") });
        await _db.ExecuteAsync(
            "INSERT INTO ho_so_khach_thue (tai_khoan_id, cccd, ngay_sinh, so_dien_thoai, que_quan) VALUES (@t, '012345678902', '2000-01-01', '0901234568', 'Ha Noi')",
            new { t = khach2Id });
        var auth = new AuthenticationService(_db, _sessions);
        var khach2Token = (await auth.LoginAsync("khach02", "mk123456")).Token;

        var r = await Call(ActionNames.SuCoTao, khach2Token, new
        {
            phongId = _phong1Id,
            mucDo = "Thap",
            moTa = "Khach chua co phong"
        });
        Assert.IsFalse(r.Success, "KhachThue khong co phong khong duoc bao su co");
    }

    [TestMethod]
    public async Task SuCo_KhanCap_XepDau_DanhSach()
    {
        await Call(ActionNames.SuCoTao, _khachToken, new { mucDo = "Thap", moTa = "Su co Thap" });
        await Call(ActionNames.SuCoTao, _khachToken, new { mucDo = "TrungBinh", moTa = "Su co TrungBinh" });
        await Call(ActionNames.SuCoTao, _khachToken, new { mucDo = "Cao", moTa = "Su co Cao" });
        await Call(ActionNames.SuCoTao, _khachToken, new { mucDo = "KhanCap", moTa = "Su co KhanCap" });

        var r = await Call(ActionNames.SuCoLayTheoKhu, _quanLyToken, new { });
        Assert.IsTrue(r.Success);
        var items = ToJson(r).EnumerateArray().ToList();
        Assert.AreEqual(4, items.Count);
        Assert.AreEqual("KhanCap", items[0].GetProperty("mucDo").GetString());
        Assert.AreEqual("Cao", items[1].GetProperty("mucDo").GetString());
        Assert.AreEqual("TrungBinh", items[2].GetProperty("mucDo").GetString());
        Assert.AreEqual("Thap", items[3].GetProperty("mucDo").GetString());
    }

    [TestMethod]
    public async Task SuCo_QuanLy_CapNhatKhuKhac_BiTuChoi()
    {
        var phongB = await _db.ExecuteAsync(
            "INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da) VALUES (@k, '201', 2000000, 2)",
            new { k = _khuB_Id });

        var r1 = await Call(ActionNames.SuCoTao, _chuTroToken, new
        {
            phongId = phongB,
            mucDo = "Thap",
            moTa = "Su co khu B"
        });
        Assert.IsTrue(r1.Success, "ChuTro bao su co: " + r1.Message);
        var scId = GetInt(r1, "suCoId");

        // QuanLy khu A cap nhat su co khu B -> phai bi tu choi
        var r2 = await Call(ActionNames.SuCoCapNhatTrangThai, _quanLyToken, new
        {
            suCoId = scId,
            trangThai = "DangXuLy"
        });
        Assert.IsFalse(r2.Success, "QuanLy khong duoc cap nhat su co khu khac");
    }

    [TestMethod]
    public async Task SuCo_QuanLy_CapNhatDaXuLy_GhiThoiGian()
    {
        var r1 = await Call(ActionNames.SuCoTao, _quanLyToken, new
        {
            phongId = _phong1Id,
            mucDo = "Cao",
            moTa = "Hong khoa cua"
        });
        Assert.IsTrue(r1.Success);
        var scId = GetInt(r1, "suCoId");

        var r2 = await Call(ActionNames.SuCoCapNhatTrangThai, _quanLyToken, new
        {
            suCoId = scId,
            trangThai = "DaXuLy",
            ketQua = "Da thay o khoa moi"
        });
        Assert.IsTrue(r2.Success, r2.Message);

        var row = await _db.QueryAsync("SELECT trang_thai, ket_qua, xu_ly_xong_luc FROM su_co WHERE id = @id", new { id = scId });
        Assert.AreEqual("DaXuLy", row[0]["trang_thai"]);
        Assert.AreEqual("Da thay o khoa moi", row[0]["ket_qua"]);
        Assert.IsNotNull(row[0]["xu_ly_xong_luc"]);
    }

    // ── BAO CAO ───────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task BaoCao_ChuTro_LayTongQuan_ThanhCong()
    {
        // Them 1 phong BaoTri + 2 hoa don (1 DaThu, 1 ChuaThu)
        await _db.ExecuteAsync(
            "INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da, trang_thai) VALUES (@k, '103', 2000000, 2, 'BaoTri')",
            new { k = _khuId });
        await _db.ExecuteAsync(
            @"INSERT INTO hoa_don (phong_id, ky_cuoc, tien_phong, tien_dien, tien_nuoc, phi_khac, tong_tien, trang_thai)
              VALUES (@p, '2026-10', 2500000, 200000, 50000, 0, 2750000, 'DaThu')",
            new { p = _phong1Id });
        await _db.ExecuteAsync(
            @"INSERT INTO hoa_don (phong_id, ky_cuoc, tien_phong, tien_dien, tien_nuoc, phi_khac, tong_tien, trang_thai)
              VALUES (@p, '2026-11', 2500000, 250000, 50000, 0, 2800000, 'ChuaThu')",
            new { p = _phong1Id });

        var r = await Call(ActionNames.BaoCaoTongQuan, _chuTroToken, new { tuThang = "2026-09", denThang = "2026-11" });
        Assert.IsTrue(r.Success, "BAO_CAO: " + r.Message);
        var el = ToJson(r);
        Assert.AreEqual(2, el.GetProperty("tongKhu").GetInt32());
        Assert.AreEqual(3, el.GetProperty("tongPhong").GetInt32());
        Assert.AreEqual(1, el.GetProperty("phongTrong").GetInt32());
        Assert.AreEqual(1, el.GetProperty("phongDangThue").GetInt32());
        Assert.AreEqual(1, el.GetProperty("phongBaoTri").GetInt32());
        Assert.AreEqual(33.3, el.GetProperty("tyLeLapDay").GetDouble());
        Assert.AreEqual(1, el.GetProperty("tongHoaDonChuaThu").GetInt32());

        var dt = el.GetProperty("doanhThu6Thang");
        Assert.AreEqual(1, dt.GetArrayLength());
        Assert.AreEqual("2026-10", dt[0].GetProperty("kyCuoc").GetString());
        Assert.AreEqual(2750000, dt[0].GetProperty("tongThu").GetInt64());
    }

    [TestMethod]
    public void RBAC_MaTranQuyen_M4b_DungTheoSpec()
    {
        var m4bActions = new[]
        {
            ActionNames.DeXuatTao, ActionNames.DeXuatLayTatCa,
            ActionNames.DeXuatDuyet, ActionNames.DeXuatTuChoi,
            ActionNames.SuCoTao, ActionNames.SuCoLayTheoKhu, ActionNames.SuCoCapNhatTrangThai,
            ActionNames.BaoCaoTongQuan,
        };

        // ChuTro: tat ca 8
        foreach (var a in m4bActions)
            Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.ChuTro, a), $"ChuTro phai co {a}");

        // CongAn: khong co action nao nhom M4b
        foreach (var a in m4bActions)
            Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.CongAn, a), $"CongAn khong duoc co {a}");

        // QuanLy: co DE_XUAT_TAO/LAY_TAT_CA, SU_CO_*; khong co DUYET/TU_CHOI/BAO_CAO_TONG_QUAN
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.QuanLy, ActionNames.DeXuatTao));
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.QuanLy, ActionNames.DeXuatLayTatCa));
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.QuanLy, ActionNames.DeXuatDuyet));
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.QuanLy, ActionNames.DeXuatTuChoi));
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.QuanLy, ActionNames.SuCoTao));
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.QuanLy, ActionNames.SuCoLayTheoKhu));
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.QuanLy, ActionNames.SuCoCapNhatTrangThai));
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.QuanLy, ActionNames.BaoCaoTongQuan));

        // NhanVien: DE_XUAT_TAO, SU_CO_TAO/LAY_THEO_KHU
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.NhanVien, ActionNames.DeXuatTao));
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.NhanVien, ActionNames.SuCoTao));
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.NhanVien, ActionNames.SuCoLayTheoKhu));
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.NhanVien, ActionNames.DeXuatDuyet));
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.NhanVien, ActionNames.BaoCaoTongQuan));

        // LeTan: SU_CO_TAO/LAY_THEO_KHU
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.LeTan, ActionNames.SuCoTao));
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.LeTan, ActionNames.SuCoLayTheoKhu));
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.LeTan, ActionNames.DeXuatTao));
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.LeTan, ActionNames.BaoCaoTongQuan));

        // KhachThue: DE_XUAT_TAO, SU_CO_TAO
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.KhachThue, ActionNames.DeXuatTao));
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.KhachThue, ActionNames.SuCoTao));
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.KhachThue, ActionNames.SuCoLayTheoKhu));
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.KhachThue, ActionNames.BaoCaoTongQuan));
    }
}
