using System.Text.Json;
using QuanLyTro.Server.Data;
using QuanLyTro.Server.Network;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Models;
using QuanLyTro.Shared.Protocol;
using QuanLyTro.Tests.TestHelpers;

namespace QuanLyTro.Tests;

[TestClass]
public class TenantTests
{
    private Database _db = null!;
    private SessionStore _sessions = null!;
    private RequestRouter _router = null!;
    private string _chuTroToken = null!;
    private int _khuId;
    private int _phong1Id;
    private int _phong2Id;

    [TestInitialize]
    public async Task Setup()
    {
        await TestDb.ResetAsync();
        _db = TestDb.Create();
        await new SchemaInitializer(_db).InitializeAsync();
        _sessions = new SessionStore();
        var auth = new AuthenticationService(_db, _sessions);
        _router = new RequestRouter(_db, _sessions);

        await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro) VALUES ('chutro', @h, 'Chu Tro', 'ChuTro')",
            new { h = PasswordHasher.Hash("chutro") });
        var s = await auth.LoginAsync("chutro", "chutro");
        _chuTroToken = s.Token;

        _khuId = await _db.ExecuteAsync(
            "INSERT INTO khu_tro (ten_khu_tro, dia_chi) VALUES ('Khu A', 'Da Nang')");
        _phong1Id = await _db.ExecuteAsync(
            "INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da) VALUES (@k, '101', 2500000, 2)",
            new { k = _khuId });
        _phong2Id = await _db.ExecuteAsync(
            "INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da) VALUES (@k, '102', 2500000, 2)",
            new { k = _khuId });
    }

    private async Task<ResponsePacket> Call(string action, string? token, object? data, string ctx = "c") =>
        await _router.HandleAsync(RequestPacket.Create(action, token, data), new ClientContext(ctx));

    private static string Raw(ResponsePacket r) => JsonDefaults.Serialize(r.Data!);

    private async Task<int> DangKyAsync(string cccd, string ten = "Khach Test")
    {
        var resp = await Call(ActionNames.KHACH_THUE_DANG_KY, null, new
        {
            tenDangNhap = cccd,
            matKhau = "mk123456",
            hoTen = ten,
            cccd,
            ngaySinh = "2000-01-01",
            soDienThoai = "0901234567",
            queQuan = "Quang Nam",
        });
        Assert.IsTrue(resp.Success, "DANG_KY: " + resp.Message);
        return GetInt(resp, "taiKhoanId");
    }

    private static int GetInt(ResponsePacket resp, string prop)
    {
        var el = JsonDefaults.Deserialize<JsonElement>(JsonDefaults.Serialize(resp.Data!));
        return el.GetProperty(prop).GetInt32();
    }

    [TestMethod]
    public async Task DangKy_Public_TaoTaiKhoanVaHoSo()
    {
        var id = await DangKyAsync("012345678901");
        var count = await _db.ScalarT<int>("SELECT COUNT(*) FROM ho_so_khach_thue WHERE tai_khoan_id = @id", new { id });
        Assert.AreEqual(1, count);
        // password phải hash PBKDF2, không lưu dạng rõ
        var hash = await _db.ScalarT<string>("SELECT mat_khau_hash FROM tai_khoan WHERE id = @id", new { id });
        StringAssert.StartsWith(hash, "PBKDF2$");
        StringAssert.DoesNotMatch(hash, new System.Text.RegularExpressions.Regex("mk123456"));
    }

    [TestMethod]
    public async Task DangKy_TrungCccd_Rollback_Sach()
    {
        await DangKyAsync("012345678901");
        var resp = await Call(ActionNames.KHACH_THUE_DANG_KY, null, new
        {
            tenDangNhap = "098765432101", // tên khác, CCCD trùng
            matKhau = "mk123456",
            hoTen = "Nguoi 2",
            cccd = "012345678901",
            ngaySinh = "2000-01-02",
            soDienThoai = "0909876543",
            queQuan = "Hoi An",
        });
        Assert.IsFalse(resp.Success);
        // Không để lại rác: chỉ 1 tai_khoan + 1 hồ sơ của lần đăng ký đầu
        var tk = await _db.ScalarT<int>("SELECT COUNT(*) FROM tai_khoan");
        Assert.AreEqual(2, tk); // chutro + khach 1
        var hs = await _db.ScalarT<int>("SELECT COUNT(*) FROM ho_so_khach_thue");
        Assert.AreEqual(1, hs);
    }

    [TestMethod]
    public async Task DangKy_CccdSaiDoDai_BiTuChoi()
    {
        var resp = await Call(ActionNames.KHACH_THUE_DANG_KY, null, new
        {
            tenDangNhap = "12345", matKhau = "mk123456", hoTen = "X",
            cccd = "12345", ngaySinh = "2000-01-01", soDienThoai = "09", queQuan = "Y",
        });
        Assert.IsFalse(resp.Success);
    }

    [TestMethod]
    public async Task ChuyenPhong_NguyenTu_GhiLichSu_2BienDong()
    {
        var tk = await DangKyAsync("012345678901");
        // gán vào phòng 1 trực tiếp
        await _db.ExecuteAsync("UPDATE ho_so_khach_thue SET phong_id = @p WHERE tai_khoan_id = @t",
            new { p = _phong1Id, t = tk });
        await _db.ExecuteAsync("INSERT INTO lich_su_cu_tru (tai_khoan_khach_id, phong_id, khu_tro_id, loai_bien_dong) VALUES (@t, @p, @k, 'NhanPhong')",
            new { t = tk, p = _phong1Id, k = _khuId });

        var resp = await Call(ActionNames.KHACH_THUE_CHUYEN_PHONG, _chuTroToken,
            new { taiKhoanId = tk, phongIdMoi = _phong2Id });
        Assert.IsTrue(resp.Success, resp.Message);

        var phongIdHienTai = await _db.ScalarT<int?>(
            "SELECT phong_id FROM ho_so_khach_thue WHERE tai_khoan_id = @t", new { t = tk });
        Assert.AreEqual(_phong2Id, phongIdHienTai);
        var soBienDong = await _db.ScalarT<int>(
            "SELECT COUNT(*) FROM lich_su_cu_tru WHERE tai_khoan_khach_id = @t", new { t = tk });
        Assert.AreEqual(3, soBienDong); // NhanPhong + ChuyenDi + ChuyenDen
    }

    [TestMethod]
    public async Task ChuyenPhong_PhongDay_BiTuChoi()
    {
        var tk1 = await DangKyAsync("012345678901");
        var tk2 = await DangKyAsync("012345678902");
        // phòng 1 sức chứa 2, đã có 2 người (trực tiếp) — chuyển tk2 vào sẽ đầy rồi
        // chuyển tk1 từ phòng 1 sang phòng 1 (chính nó) → không đổi, nhưng thử sức chứa đầy:
        await _db.ExecuteAsync("UPDATE ho_so_khach_thue SET phong_id = @p WHERE tai_khoan_id IN (@a, @b)",
            new { p = _phong1Id, a = tk1, b = tk2 });
        var tk3 = await DangKyAsync("012345678903");
        var resp = await Call(ActionNames.KHACH_THUE_CHUYEN_PHONG, _chuTroToken,
            new { taiKhoanId = tk3, phongIdMoi = _phong1Id });
        Assert.IsFalse(resp.Success);
        StringAssert.Contains(resp.Message, "đầy");
    }

    [TestMethod]
    public async Task TraPhong_Idempotent_HoSoRong()
    {
        var tk = await DangKyAsync("012345678901");
        await _db.ExecuteAsync("UPDATE ho_so_khach_thue SET phong_id = @p WHERE tai_khoan_id = @t",
            new { p = _phong1Id, t = tk });
        var r1 = await Call(ActionNames.KHACH_THUE_TRA_PHONG, _chuTroToken, new { taiKhoanId = tk });
        Assert.IsTrue(r1.Success, r1.Message);
        var r2 = await Call(ActionNames.KHACH_THUE_TRA_PHONG, _chuTroToken, new { taiKhoanId = tk });
        Assert.IsTrue(r2.Success, "gọi lần 2 vẫn ok (idempotent): " + r2.Message);
        var phongId = await _db.ScalarT<int?>(
            "SELECT phong_id FROM ho_so_khach_thue WHERE tai_khoan_id = @t", new { t = tk });
        Assert.IsNull(phongId);
    }

    [TestMethod]
    public async Task TimKiem_Toida50_KeywordRong_Sai()
    {
        for (var i = 0; i < 5; i++)
            await DangKyAsync($"0123456789{i:D2}", $"Khach {i}");
        var resp = await Call(ActionNames.KHACH_THUE_TIM_KIEM, _chuTroToken, new { keyword = "Khach" });
        Assert.IsTrue(resp.Success, resp.Message);
        Assert.IsTrue(Raw(resp).Contains("Khach"));

        var rong = await Call(ActionNames.KHACH_THUE_TIM_KIEM, _chuTroToken, new { keyword = "" });
        Assert.IsFalse(rong.Success);

        var dacbiet = await Call(ActionNames.KHACH_THUE_TIM_KIEM, _chuTroToken, new { keyword = "%" });
        Assert.IsTrue(dacbiet.Success, "ký tự % phải được escape, không trả toàn bộ");
        Assert.AreEqual("[]", Raw(dacbiet), "% không khớp ai cả");
    }

    [TestMethod]
    public async Task KhachThue_XemHoSoCuaToi_KhongThayHoSoNguoiKhac()
    {
        var tk = await DangKyAsync("012345678901");
        var auth = new AuthenticationService(_db, _sessions);
        var s = await auth.LoginAsync("012345678901", "mk123456");
        var resp = await Call(ActionNames.KHACH_THUE_HO_SO_CUA_TOI, s.Token, null);
        Assert.IsTrue(resp.Success, resp.Message);
        var json = Raw(resp);
        StringAssert.Contains(json, "Khach Test");
    }
}
