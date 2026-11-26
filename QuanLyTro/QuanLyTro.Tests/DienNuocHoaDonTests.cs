using System.Text.Json;
using QuanLyTro.Server.Data;
using QuanLyTro.Server.Network;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Models;
using QuanLyTro.Shared.Protocol;
using QuanLyTro.Tests.TestHelpers;

namespace QuanLyTro.Tests;

/// <summary>
/// Sprint M4a: DIEN_NUOC_* (chốt chỉ số BR-07) + HOA_DON_* (lập/thu BR-11) + DOI_SOAT_CHAY.
/// Chạy qua RequestRouter như client thật, kiểm tra cả RBAC lẫn tính toán tiền.
/// </summary>
[TestClass]
public class DienNuocHoaDonTests
{
    private Database _db = null!;
    private SessionStore _sessions = null!;
    private RequestRouter _router = null!;
    private AuthenticationService _auth = null!;
    private string _chuTroToken = null!;
    private int _khuId;
    private int _phong1Id;
    private int _phong2Id;
    private int _khu2Id;
    private int _phongKhu2Id;
    private string _tokenQLKhu1 = null!;
    private string _tokenQLKhu2 = null!;

    [TestInitialize]
    public async Task Setup()
    {
        await TestDb.ResetAsync();
        _db = TestDb.Create();
        await new SchemaInitializer(_db).InitializeAsync();
        _sessions = new SessionStore();
        _auth = new AuthenticationService(_db, _sessions);
        _router = new RequestRouter(_db, _sessions);

        await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro) VALUES ('chutro', @h, 'Chu Tro', 'ChuTro')",
            new { h = PasswordHasher.Hash("chutro") });
        var s = await _auth.LoginAsync("chutro", "chutro");
        _chuTroToken = s.Token;

        _khuId = await _db.ExecuteAsync(
            "INSERT INTO khu_tro (ten_khu_tro, dia_chi) VALUES ('Khu A', 'Da Nang')");
        _phong1Id = await _db.ExecuteAsync(
            "INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da) VALUES (@k, '101', 2500000, 2)",
            new { k = _khuId });
        _phong2Id = await _db.ExecuteAsync(
            "INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da) VALUES (@k, '102', 3000000, 2)",
            new { k = _khuId });

        _khu2Id = await _db.ExecuteAsync(
            "INSERT INTO khu_tro (ten_khu_tro, dia_chi) VALUES ('Khu B', 'Hoi An')");
        _phongKhu2Id = await _db.ExecuteAsync(
            "INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da) VALUES (@k, '201', 4000000, 3)",
            new { k = _khu2Id });

        _tokenQLKhu1 = await TaoVaLoginQLAsync("qlkhu1", _khuId);
        _tokenQLKhu2 = await TaoVaLoginQLAsync("qlkhu2", _khu2Id);
    }

    private async Task<string> TaoVaLoginQLAsync(string ten, int khuId)
    {
        await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro, khu_tro_id) VALUES (@t, @h, @t, 'QuanLy', @k)",
            new { t = ten, h = PasswordHasher.Hash(ten), k = khuId });
        var s = await _auth.LoginAsync(ten, ten);
        return s.Token;
    }

    private async Task<ResponsePacket> Call(string action, string? token, object? data, string ctx = "c") =>
        await _router.HandleAsync(RequestPacket.Create(action, token, data), new ClientContext(ctx));

    private static string Raw(ResponsePacket r) => JsonDefaults.Serialize(r.Data!);

    private static JsonElement El(ResponsePacket r) =>
        JsonDefaults.Deserialize<JsonElement>(JsonDefaults.Serialize(r.Data!));

    /// <summary>Ghi chỉ số qua action DIEN_NUOC_GHI_SO (đúng payload UI utils.js gửi).</summary>
    private async Task<ResponsePacket> GhiSoAsync(string token, int phongId, string ky,
        int dienCu, int dienMoi, int nuocCu, int nuocMoi, string hinhThuc = "Khoi", int soNguoi = 0,
        long giaDien = 3500, long giaNuoc = 12000) =>
        await Call(ActionNames.DIEN_NUOC_GHI_SO, token, new
        {
            phongId, kyCuoc = ky, dienCu, dienMoi, giaDien,
            nuocCu, nuocMoi, giaNuoc, hinhThucNuoc = hinhThuc, soNguoiNuoc = soNguoi,
        });

    private async Task<ResponsePacket> LapHoaDonAsync(string token, int phongId, string ky, long phiKhac = 0) =>
        await Call(ActionNames.HOA_DON_TAO, token, new { phongId, kyCuoc = ky, phiKhac });

    // ---------- RBAC ma trận quyền ----------

    [TestMethod]
    public async Task RBAC_QuanLy_DuocGhiSoVaLapHoaDon()
    {
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.QuanLy, ActionNames.DIEN_NUOC_LAY_KY_TRUOC));
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.QuanLy, ActionNames.DIEN_NUOC_GHI_SO));
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.QuanLy, ActionNames.HOA_DON_TAO));
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.QuanLy, ActionNames.HOA_DON_LAY_TAT_CA));
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.QuanLy, ActionNames.HOA_DON_THANH_TOAN));
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.QuanLy, ActionNames.DOI_SOAT_CHAY));
    }

    [TestMethod]
    public async Task RBAC_KhachThue_ChiXemHoaDonCuaToi()
    {
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.KhachThue, ActionNames.HOA_DON_CUA_TOI));
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.KhachThue, ActionNames.HOA_DON_TAO));
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.KhachThue, ActionNames.HOA_DON_THANH_TOAN));
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.KhachThue, ActionNames.DIEN_NUOC_GHI_SO));
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.KhachThue, ActionNames.DOI_SOAT_CHAY));
    }

    [TestMethod]
    public async Task RBAC_LeTanVaCongAn_KhongCoQuyenNhomDienNuocHoaDon()
    {
        foreach (var role in new[] { VaiTroNguoiDung.LeTan, VaiTroNguoiDung.NhanVien, VaiTroNguoiDung.CongAn })
        {
            Assert.IsFalse(DefaultPermissions.Allowed(role, ActionNames.DIEN_NUOC_GHI_SO), role.ToString());
            Assert.IsFalse(DefaultPermissions.Allowed(role, ActionNames.HOA_DON_TAO), role.ToString());
            Assert.IsFalse(DefaultPermissions.Allowed(role, ActionNames.HOA_DON_CUA_TOI), role.ToString());
            Assert.IsFalse(DefaultPermissions.Allowed(role, ActionNames.DOI_SOAT_CHAY), role.ToString());
        }
    }

    [TestMethod]
    public async Task RBAC_LeTan_GoiGhiSo_QuaRouterBiTuChoi()
    {
        await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro) VALUES ('letan', @h, 'Le Tan', 'LeTan')",
            new { h = PasswordHasher.Hash("letan") });
        var token = (await _auth.LoginAsync("letan", "letan")).Token;
        var resp = await GhiSoAsync(token, _phong1Id, "2026-11", 0, 10, 0, 5);
        Assert.IsFalse(resp.Success);
    }

    // ---------- DIEN_NUOC_LAY_KY_TRUOC ----------

    [TestMethod]
    public async Task LayKyTruoc_ChuaCoDuLieu_TraGiaTri0()
    {
        var resp = await Call(ActionNames.DIEN_NUOC_LAY_KY_TRUOC, _chuTroToken,
            new { phongId = _phong1Id, kyCuoc = "2026-11" });
        Assert.IsTrue(resp.Success, resp.Message);
        var el = El(resp);
        Assert.AreEqual(0, el.GetProperty("dienMoi").GetInt32());
        Assert.AreEqual(0, el.GetProperty("nuocMoi").GetInt32());
    }

    [TestMethod]
    public async Task LayKyTruoc_CoKyTruoc_TraDienMoiKyTruoc()
    {
        await GhiSoAsync(_chuTroToken, _phong1Id, "2026-10", 100, 150, 10, 15);
        var resp = await Call(ActionNames.DIEN_NUOC_LAY_KY_TRUOC, _chuTroToken,
            new { phongId = _phong1Id, kyCuoc = "2026-11" });
        Assert.IsTrue(resp.Success, resp.Message);
        var el = El(resp);
        Assert.AreEqual(150, el.GetProperty("dienMoi").GetInt32());
        Assert.AreEqual(15, el.GetProperty("nuocMoi").GetInt32());
        Assert.AreEqual(3500, el.GetProperty("giaDien").GetInt64());
        Assert.AreEqual(12000, el.GetProperty("giaNuoc").GetInt64());
    }

    [TestMethod]
    public async Task LayKyTruoc_QuanLyKhuKhac_BiTuChoi()
    {
        await GhiSoAsync(_chuTroToken, _phong1Id, "2026-10", 0, 10, 0, 5);
        var resp = await Call(ActionNames.DIEN_NUOC_LAY_KY_TRUOC, _tokenQLKhu2,
            new { phongId = _phong1Id, kyCuoc = "2026-11" });
        Assert.IsFalse(resp.Success);
    }

    // ---------- DIEN_NUOC_GHI_SO ----------

    [TestMethod]
    public async Task GhiSo_LanDau_TaoBanGhi()
    {
        var resp = await GhiSoAsync(_chuTroToken, _phong1Id, "2026-11", 100, 150, 10, 15);
        Assert.IsTrue(resp.Success, resp.Message);
        var count = await _db.ScalarT<int>(
            "SELECT COUNT(*) FROM chi_so_dien_nuoc WHERE phong_id = @p AND ky_cuoc = @ky",
            new { p = _phong1Id, ky = "2026-11" });
        Assert.AreEqual(1, count);
        var dienMoi = await _db.ScalarT<int>(
            "SELECT dien_moi FROM chi_so_dien_nuoc WHERE phong_id = @p AND ky_cuoc = @ky",
            new { p = _phong1Id, ky = "2026-11" });
        Assert.AreEqual(150, dienMoi);
    }

    [TestMethod]
    public async Task GhiSo_Lan2_UpSert_KhongNhanBan()
    {
        await GhiSoAsync(_chuTroToken, _phong1Id, "2026-11", 100, 150, 10, 15);
        var resp = await GhiSoAsync(_chuTroToken, _phong1Id, "2026-11", 100, 160, 10, 18);
        Assert.IsTrue(resp.Success, resp.Message);
        var count = await _db.ScalarT<int>(
            "SELECT COUNT(*) FROM chi_so_dien_nuoc WHERE phong_id = @p AND ky_cuoc = @ky",
            new { p = _phong1Id, ky = "2026-11" });
        Assert.AreEqual(1, count, "UPSERT không được nhân bản dòng");
        var dienMoi = await _db.ScalarT<int>(
            "SELECT dien_moi FROM chi_so_dien_nuoc WHERE phong_id = @p AND ky_cuoc = @ky",
            new { p = _phong1Id, ky = "2026-11" });
        Assert.AreEqual(160, dienMoi, "ghi lại phải cập nhật chỉ số mới");
    }

    [TestMethod]
    public async Task GhiSo_DienMoiNhoHonDienCu_BiChan()
    {
        var resp = await GhiSoAsync(_chuTroToken, _phong1Id, "2026-11", 150, 100, 10, 15);
        Assert.IsFalse(resp.Success, "BR-07: chỉ số mới < chỉ số cũ phải bị chặn");
    }

    [TestMethod]
    public async Task GhiSo_NuocKhoi_NuocMoiNhoHonNuocCu_BiChan()
    {
        var resp = await GhiSoAsync(_chuTroToken, _phong1Id, "2026-11", 0, 10, 20, 5, hinhThuc: "Khoi");
        Assert.IsFalse(resp.Success);
    }

    [TestMethod]
    public async Task GhiSo_Nguoi_KhongNhanBanNuocCu_NuocMoiBangNuocCu()
    {
        // UI mode 'Nguoi' gửi nuocMoi = nuocCu (không dùng đồng hồ nước)
        var resp = await GhiSoAsync(_chuTroToken, _phong1Id, "2026-11", 0, 10, 20, 20,
            hinhThuc: "Nguoi", soNguoi: 3);
        Assert.IsTrue(resp.Success, resp.Message);
    }

    [TestMethod]
    public async Task GhiSo_QuanLyKhuKhac_BiTuChoi()
    {
        var resp = await GhiSoAsync(_tokenQLKhu2, _phong1Id, "2026-11", 0, 10, 0, 5);
        Assert.IsFalse(resp.Success);
        var count = await _db.ScalarT<int>("SELECT COUNT(*) FROM chi_so_dien_nuoc");
        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public async Task GhiSo_QuanLyDungKhu_OK()
    {
        var resp = await GhiSoAsync(_tokenQLKhu1, _phong1Id, "2026-11", 0, 10, 0, 5);
        Assert.IsTrue(resp.Success, resp.Message);
    }

    [TestMethod]
    public async Task GhiSo_KyCuocSaiDinhDang_BiChan()
    {
        var resp = await GhiSoAsync(_chuTroToken, _phong1Id, "11/2026", 0, 10, 0, 5);
        Assert.IsFalse(resp.Success, "kyCuoc phải là yyyy-MM");
    }

    // ---------- HOA_DON_TAO ----------

    [TestMethod]
    public async Task LapHoaDon_TinhTienDienNuoc_DungCongThuc()
    {
        await GhiSoAsync(_chuTroToken, _phong1Id, "2026-11", 100, 150, 10, 15);
        var resp = await LapHoaDonAsync(_chuTroToken, _phong1Id, "2026-11");
        Assert.IsTrue(resp.Success, resp.Message);
        var el = El(resp);
        // Fallback gia_thue phong (M3 hop_dong chưa merge): 2.500.000
        Assert.AreEqual(2500000L, el.GetProperty("tienPhong").GetInt64());
        // (150-100) × 3500
        Assert.AreEqual(175000L, el.GetProperty("tienDien").GetInt64());
        // (15-10) × 12000
        Assert.AreEqual(60000L, el.GetProperty("tienNuoc").GetInt64());
        Assert.AreEqual(0L, el.GetProperty("phiKhac").GetInt64());
        Assert.AreEqual(2735000L, el.GetProperty("tongTien").GetInt64());
        Assert.AreEqual("ChuaThu", el.GetProperty("trangThai").GetString());
    }

    [TestMethod]
    public async Task LapHoaDon_NuocTheoNguoi_TinhSoNguoiNhanGia()
    {
        await GhiSoAsync(_chuTroToken, _phong1Id, "2026-11", 0, 10, 20, 20, hinhThuc: "Nguoi", soNguoi: 3);
        var resp = await LapHoaDonAsync(_chuTroToken, _phong1Id, "2026-11");
        Assert.IsTrue(resp.Success, resp.Message);
        var el = El(resp);
        Assert.AreEqual(36000L, el.GetProperty("tienNuoc").GetInt64(), "3 người × 12000");
    }

    [TestMethod]
    public async Task LapHoaDon_NuocDauNguoi_NguyenTacGiongNguoi()
    {
        await GhiSoAsync(_chuTroToken, _phong2Id, "2026-11", 0, 10, 0, 0, hinhThuc: "DauNguoi", soNguoi: 2);
        var resp = await LapHoaDonAsync(_chuTroToken, _phong2Id, "2026-11");
        Assert.IsTrue(resp.Success, resp.Message);
        var el = El(resp);
        Assert.AreEqual(24000L, el.GetProperty("tienNuoc").GetInt64(), "2 người × 12000");
    }

    [TestMethod]
    public async Task LapHoaDon_PhiKhac_CongVaoTong()
    {
        await GhiSoAsync(_chuTroToken, _phong1Id, "2026-11", 0, 0, 0, 0);
        var resp = await LapHoaDonAsync(_chuTroToken, _phong1Id, "2026-11", phiKhac: 50000);
        Assert.IsTrue(resp.Success, resp.Message);
        var el = El(resp);
        Assert.AreEqual(50000L, el.GetProperty("phiKhac").GetInt64());
        Assert.AreEqual(2550000L, el.GetProperty("tongTien").GetInt64());
    }

    [TestMethod]
    public async Task LapHoaDon_ChuaGhiChiSo_BiChan()
    {
        var resp = await LapHoaDonAsync(_chuTroToken, _phong1Id, "2026-11");
        Assert.IsFalse(resp.Success);
        StringAssert.Contains(resp.Message, "chỉ số");
    }

    [TestMethod]
    public async Task LapHoaDon_TrungPhongKy_BiChan()
    {
        await GhiSoAsync(_chuTroToken, _phong1Id, "2026-11", 0, 10, 0, 5);
        var r1 = await LapHoaDonAsync(_chuTroToken, _phong1Id, "2026-11");
        Assert.IsTrue(r1.Success, r1.Message);
        var r2 = await LapHoaDonAsync(_chuTroToken, _phong1Id, "2026-11");
        Assert.IsFalse(r2.Success, "(phòng, kỳ) trùng phải bị chặn");
        var count = await _db.ScalarT<int>(
            "SELECT COUNT(*) FROM hoa_don WHERE phong_id = @p AND ky_cuoc = @ky",
            new { p = _phong1Id, ky = "2026-11" });
        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public async Task LapHoaDon_QuanLyPhongKhuKhac_BiTuChoi()
    {
        await GhiSoAsync(_chuTroToken, _phong1Id, "2026-11", 0, 10, 0, 5);
        var resp = await LapHoaDonAsync(_tokenQLKhu2, _phong1Id, "2026-11");
        Assert.IsFalse(resp.Success);
        var count = await _db.ScalarT<int>("SELECT COUNT(*) FROM hoa_don");
        Assert.AreEqual(0, count);
    }

    // ---------- HOA_DON_LAY_TAT_CA ----------

    [TestMethod]
    public async Task LayHoaDon_LocKyVaPhong_DungTruongCamelCase()
    {
        await GhiSoAsync(_chuTroToken, _phong1Id, "2026-11", 0, 10, 0, 5);
        await GhiSoAsync(_chuTroToken, _phong2Id, "2026-11", 0, 10, 0, 5);
        await GhiSoAsync(_chuTroToken, _phong1Id, "2026-10", 0, 5, 0, 3);
        await LapHoaDonAsync(_chuTroToken, _phong1Id, "2026-11");
        await LapHoaDonAsync(_chuTroToken, _phong2Id, "2026-11");
        await LapHoaDonAsync(_chuTroToken, _phong1Id, "2026-10");

        var thang11 = await Call(ActionNames.HOA_DON_LAY_TAT_CA, _chuTroToken, new { kyCuoc = "2026-11" });
        Assert.IsTrue(thang11.Success, thang11.Message);
        var arr = El(thang11);
        Assert.AreEqual(2, arr.GetArrayLength(), "lọc theo kỳ");
        var item0 = arr[0];
        Assert.AreEqual(2500000L, item0.GetProperty("tienPhong").GetInt64());
        Assert.AreEqual(35000L, item0.GetProperty("tienDien").GetInt64());
        Assert.AreEqual(60000L, item0.GetProperty("tienNuoc").GetInt64());
        Assert.AreEqual("ChuaThu", item0.GetProperty("trangThai").GetString());

        var phong1 = await Call(ActionNames.HOA_DON_LAY_TAT_CA, _chuTroToken,
            new { kyCuoc = "2026-11", phongId = _phong1Id });
        var arr1 = El(phong1);
        Assert.AreEqual(1, arr1.GetArrayLength(), "lọc theo phòng");
    }

    [TestMethod]
    public async Task LayHoaDon_QuanLyChiThayKhuCuaMinh()
    {
        await GhiSoAsync(_chuTroToken, _phong1Id, "2026-11", 0, 10, 0, 5);
        await GhiSoAsync(_chuTroToken, _phongKhu2Id, "2026-11", 0, 10, 0, 5);
        await LapHoaDonAsync(_chuTroToken, _phong1Id, "2026-11");
        await LapHoaDonAsync(_chuTroToken, _phongKhu2Id, "2026-11");

        var resp = await Call(ActionNames.HOA_DON_LAY_TAT_CA, _tokenQLKhu1, new { kyCuoc = "2026-11" });
        Assert.IsTrue(resp.Success, resp.Message);
        var arr = El(resp);
        Assert.AreEqual(1, arr.GetArrayLength(), "QuanLy chỉ thấy hóa đơn khu mình");
        Assert.AreEqual(_phong1Id, arr[0].GetProperty("phongId").GetInt32());
    }

    // ---------- HOA_DON_THANH_TOAN ----------

    [TestMethod]
    public async Task ThuTien_ChuaThu_ChuyenDaThu_GhiNgayDong()
    {
        await GhiSoAsync(_chuTroToken, _phong1Id, "2026-11", 0, 10, 0, 5);
        await LapHoaDonAsync(_chuTroToken, _phong1Id, "2026-11");
        var hdId = await _db.ScalarT<int>("SELECT id FROM hoa_don WHERE phong_id = @p", new { p = _phong1Id });

        var resp = await Call(ActionNames.HOA_DON_THANH_TOAN, _chuTroToken, new { hoaDonId = hdId });
        Assert.IsTrue(resp.Success, resp.Message);
        var trangThai = await _db.ScalarT<string>(
            "SELECT trang_thai FROM hoa_don WHERE id = @id", new { id = hdId });
        Assert.AreEqual("DaThu", trangThai);
        var ngayDong = await _db.ScalarT<DateTime?>(
            "SELECT ngay_dong FROM hoa_don WHERE id = @id", new { id = hdId });
        Assert.IsNotNull(ngayDong);
        var nguoiThu = await _db.ScalarT<int?>(
            "SELECT nguoi_thu_id FROM hoa_don WHERE id = @id", new { id = hdId });
        Assert.IsNotNull(nguoiThu);
    }

    [TestMethod]
    public async Task ThuTien_DaThu_BiChan_HoaDonBatBian()
    {
        await GhiSoAsync(_chuTroToken, _phong1Id, "2026-11", 0, 10, 0, 5);
        await LapHoaDonAsync(_chuTroToken, _phong1Id, "2026-11");
        var hdId = await _db.ScalarT<int>("SELECT id FROM hoa_don WHERE phong_id = @p", new { p = _phong1Id });

        var r1 = await Call(ActionNames.HOA_DON_THANH_TOAN, _chuTroToken, new { hoaDonId = hdId });
        Assert.IsTrue(r1.Success, r1.Message);
        var r2 = await Call(ActionNames.HOA_DON_THANH_TOAN, _chuTroToken, new { hoaDonId = hdId });
        Assert.IsFalse(r2.Success, "BR-11: thu lại hóa đơn đã thu phải báo lỗi");
    }

    [TestMethod]
    public async Task ThuTien_HoaDonKhongTonTai_BiChan()
    {
        var resp = await Call(ActionNames.HOA_DON_THANH_TOAN, _chuTroToken, new { hoaDonId = 99999 });
        Assert.IsFalse(resp.Success);
    }

    [TestMethod]
    public async Task ThuTien_QuanLyChiThuHoaDonKhuMinh()
    {
        await GhiSoAsync(_chuTroToken, _phongKhu2Id, "2026-11", 0, 10, 0, 5);
        await LapHoaDonAsync(_chuTroToken, _phongKhu2Id, "2026-11");
        var hdId = await _db.ScalarT<int>("SELECT id FROM hoa_don WHERE phong_id = @p", new { p = _phongKhu2Id });
        var resp = await Call(ActionNames.HOA_DON_THANH_TOAN, _tokenQLKhu1, new { hoaDonId = hdId });
        Assert.IsFalse(resp.Success, "QuanLy khu A không được thu hóa đơn khu B");
    }

    // ---------- HOA_DON_CUA_TOI ----------

    private async Task<int> DangKyVaVoPhongAsync(string cccd, int phongId)
    {
        var resp = await Call(ActionNames.KHACH_THUE_DANG_KY, null, new
        {
            tenDangNhap = cccd, matKhau = "mk123456", hoTen = "Khach " + cccd, cccd,
            ngaySinh = "2000-01-01", soDienThoai = "0901234567", queQuan = "Da Nang",
        });
        Assert.IsTrue(resp.Success, "DANG_KY: " + resp.Message);
        var id = El(resp).GetProperty("taiKhoanId").GetInt32();
        await _db.ExecuteAsync(
            "UPDATE ho_so_khach_thue SET phong_id = @p WHERE tai_khoan_id = @t",
            new { p = phongId, t = id });
        return id;
    }

    [TestMethod]
    public async Task HoaDonCuaToi_PhanTrang_DungShape()
    {
        var tk = await DangKyVaVoPhongAsync("012345678901", _phong1Id);
        foreach (var ky in new[] { "2026-08", "2026-09", "2026-10" })
        {
            await GhiSoAsync(_chuTroToken, _phong1Id, ky, 0, 10, 0, 5);
            await LapHoaDonAsync(_chuTroToken, _phong1Id, ky);
        }

        var token = (await _auth.LoginAsync("012345678901", "mk123456")).Token;
        var resp = await Call(ActionNames.HOA_DON_CUA_TOI, token, new { page = 1, soLuongMoiTrang = 2 });
        Assert.IsTrue(resp.Success, resp.Message);
        var el = El(resp);
        Assert.AreEqual(3, el.GetProperty("tongSo").GetInt64());
        Assert.AreEqual(1, el.GetProperty("trang").GetInt64());
        Assert.AreEqual(2, el.GetProperty("soLuongMoiTrang").GetInt64());
        Assert.AreEqual(_phong1Id, el.GetProperty("phongId").GetInt32());
        Assert.AreEqual("101", el.GetProperty("soPhong").GetString());
        var danhSach = el.GetProperty("danhSach");
        Assert.AreEqual(2, danhSach.GetArrayLength(), "trang 1 có tối đa 2 bản ghi");
        Assert.AreEqual("2026-10", danhSach[0].GetProperty("kyCuoc").GetString(), "mới nhất trước");
        Assert.IsNotNull(danhSach[0].GetProperty("tongTien"));
        Assert.IsNotNull(danhSach[0].GetProperty("trangThai"));
    }

    [TestMethod]
    public async Task HoaDonCuaToi_ChiNhanHoaDonPhongCuaMinh()
    {
        var tkA = await DangKyVaVoPhongAsync("012345678901", _phong1Id);
        var tkB = await DangKyVaVoPhongAsync("012345678902", _phong2Id);
        await GhiSoAsync(_chuTroToken, _phong1Id, "2026-11", 0, 10, 0, 5);
        await GhiSoAsync(_chuTroToken, _phong2Id, "2026-11", 0, 10, 0, 5);
        await LapHoaDonAsync(_chuTroToken, _phong1Id, "2026-11");
        await LapHoaDonAsync(_chuTroToken, _phong2Id, "2026-11");

        var tokenA = (await _auth.LoginAsync("012345678901", "mk123456")).Token;
        var resp = await Call(ActionNames.HOA_DON_CUA_TOI, tokenA, new { page = 1, soLuongMoiTrang = 10 });
        Assert.IsTrue(resp.Success, resp.Message);
        var el = El(resp);
        Assert.AreEqual(1, el.GetProperty("tongSo").GetInt64(), "chỉ thấy hóa đơn phòng mình");
        Assert.AreEqual(_phong1Id, el.GetProperty("phongId").GetInt32());
    }

    [TestMethod]
    public async Task HoaDonCuaToi_KhachChuaCoPhong_DanhSachRong()
    {
        var resp = await Call(ActionNames.KHACH_THUE_DANG_KY, null, new
        {
            tenDangNhap = "012345678909", matKhau = "mk123456", hoTen = "Khach Khong Phong",
            cccd = "012345678909", ngaySinh = "2000-01-01", soDienThoai = "0901234567", queQuan = "DN",
        });
        Assert.IsTrue(resp.Success, resp.Message);
        var token = (await _auth.LoginAsync("012345678909", "mk123456")).Token;
        var resp2 = await Call(ActionNames.HOA_DON_CUA_TOI, token, new { page = 1, soLuongMoiTrang = 10 });
        Assert.IsTrue(resp2.Success, "chưa có phòng vẫn phải trả về rông, không phải lỗi: " + resp2.Message);
        var el = El(resp2);
        Assert.AreEqual(0, el.GetProperty("tongSo").GetInt64());
        Assert.IsNull(JsonDefaults.Deserialize<JsonElement>(Raw(resp2)).TryGetProperty("phongId", out var pid) && pid.ValueKind != JsonValueKind.Null ? pid : (JsonElement?)null);
        Assert.AreEqual(0, el.GetProperty("danhSach").GetArrayLength());
    }

    [TestMethod]
    public async Task HoaDonCuaToi_ChiSoKyNay_TraCungDuLieuChiSo()
    {
        var tk = await DangKyVaVoPhongAsync("012345678901", _phong1Id);
        await GhiSoAsync(_chuTroToken, _phong1Id, "2026-11", 100, 150, 10, 15);
        await LapHoaDonAsync(_chuTroToken, _phong1Id, "2026-11");

        var token = (await _auth.LoginAsync("012345678901", "mk123456")).Token;
        var resp = await Call(ActionNames.HOA_DON_CUA_TOI, token, new { page = 1, soLuongMoiTrang = 10 });
        Assert.IsTrue(resp.Success, resp.Message);
        var el = El(resp);
        Assert.IsTrue(el.TryGetProperty("chiSoKyNay", out var cs) && cs.ValueKind == JsonValueKind.Object,
            "phải trả chiSoKyNay cho phòng đang ở");
        Assert.AreEqual(100, cs.GetProperty("dienCu").GetInt32());
        Assert.AreEqual(150, cs.GetProperty("dienMoi").GetInt32());
        Assert.AreEqual(3500, cs.GetProperty("giaDien").GetInt64());
        Assert.AreEqual("Khoi", cs.GetProperty("hinhThucNuoc").GetString());
    }

    [TestMethod]
    public async Task HoaDonCuaToi_KhachThayTrangThaiSauKhiChuTroThu()
    {
        var tk = await DangKyVaVoPhongAsync("012345678901", _phong1Id);
        await GhiSoAsync(_chuTroToken, _phong1Id, "2026-11", 0, 10, 0, 5);
        await LapHoaDonAsync(_chuTroToken, _phong1Id, "2026-11");

        var token = (await _auth.LoginAsync("012345678901", "mk123456")).Token;
        var truoc = await Call(ActionNames.HOA_DON_CUA_TOI, token, new { page = 1, soLuongMoiTrang = 10 });
        Assert.AreEqual("ChuaThu", El(truoc).GetProperty("danhSach")[0].GetProperty("trangThai").GetString());

        var hdId = await _db.ScalarT<int>("SELECT id FROM hoa_don WHERE phong_id = @p", new { p = _phong1Id });
        var thu = await Call(ActionNames.HOA_DON_THANH_TOAN, _chuTroToken, new { hoaDonId = hdId });
        Assert.IsTrue(thu.Success, thu.Message);

        var sau = await Call(ActionNames.HOA_DON_CUA_TOI, token, new { page = 1, soLuongMoiTrang = 10 });
        Assert.AreEqual("DaThu", El(sau).GetProperty("danhSach")[0].GetProperty("trangThai").GetString(),
            "khách thấy hóa đơn Đã thanh toán sau khi chủ trọ thu tiền");
        Assert.IsNotNull(El(sau).GetProperty("danhSach")[0].GetProperty("ngayDong"));
    }

    // ---------- DOI_SOAT_CHAY ----------

    [TestMethod]
    public async Task DoiSoat_KhongCoApiToken_TraZerosKhongLoi()
    {
        var resp = await Call(ActionNames.DOI_SOAT_CHAY, _chuTroToken, new { });
        Assert.IsTrue(resp.Success, resp.Message);
        var el = El(resp);
        Assert.AreEqual(0, el.GetProperty("soGiaoDichMoi").GetInt64());
        Assert.AreEqual(0, el.GetProperty("soKhop").GetInt64());
        Assert.AreEqual(0, el.GetProperty("soBoQua").GetInt64());
    }

    [TestMethod]
    public async Task DoiSoat_KhachThueGoi_BiTuChoi()
    {
        var tk = await DangKyVaVoPhongAsync("012345678901", _phong1Id);
        var token = (await _auth.LoginAsync("012345678901", "mk123456")).Token;
        var resp = await Call(ActionNames.DOI_SOAT_CHAY, token, new { });
        Assert.IsFalse(resp.Success, "khách thuê không có quyền đối soát");
    }
}
