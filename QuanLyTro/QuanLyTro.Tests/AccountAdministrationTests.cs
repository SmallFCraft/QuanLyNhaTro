using QuanLyTro.Server.Data;
using QuanLyTro.Server.Network;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Protocol;
using QuanLyTro.Tests.TestHelpers;

namespace QuanLyTro.Tests;

[TestClass]
public class AccountAdministrationTests
{
    private Database _db = null!;
    private RequestRouter _router = null!;
    private string _ownerToken = null!;
    private int _siteId;
    private int _managerId;

    [TestInitialize]
    public async Task Setup()
    {
        await TestDb.ResetAsync();
        _db = TestDb.Create();
        await new SchemaInitializer(_db).InitializeAsync();
        var sessions = new SessionStore();
        var auth = new AuthenticationService(_db, sessions);
        _router = new RequestRouter(_db, sessions);
        await _db.ExecuteAsync("INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro) VALUES ('owner', @h, 'Owner', 'ChuTro')", new { h = PasswordHasher.Hash("owner") });
        _siteId = await _db.ExecuteAsync("INSERT INTO khu_tro (ten_khu_tro, dia_chi) VALUES ('Site A','Address')");
        _managerId = await _db.ExecuteAsync("INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro) VALUES ('account1', @h, 'Account', 'LeTan')", new { h = PasswordHasher.Hash("secret1") });
        _ownerToken = (await auth.LoginAsync("owner", "owner")).Token;
    }

    private Task<ResponsePacket> Call(string action, object data) =>
        _router.HandleAsync(RequestPacket.Create(action, _ownerToken, data), new ClientContext("test"));

    [TestMethod]
    public async Task ChuTro_CapNhatTaiKhoan_ThayDoiVaiTroTrangThaiVaKhu()
    {
        var resp = await Call(ActionNames.TAI_KHOAN_CAP_NHAT, new
        {
            taiKhoanId = _managerId,
            hoTen = "Updated",
            vaiTro = "QuanLy",
            biKhoa = true,
            lyDoKhoa = "Tạm ngưng",
            khuTroId = _siteId,
        });
        Assert.IsTrue(resp.Success, resp.Message);
        var row = (await _db.QueryAsync("SELECT ho_ten, vai_tro, bi_khoa, ly_do_khoa, khu_tro_id FROM tai_khoan WHERE id=@id", new { id = _managerId }))[0];
        Assert.AreEqual("Updated", row["ho_ten"]);
        Assert.AreEqual("QuanLy", row["vai_tro"]);
        Assert.AreEqual(1, Convert.ToInt32(row["bi_khoa"]));
        Assert.AreEqual("Tạm ngưng", row["ly_do_khoa"]);
        Assert.AreEqual(_siteId, Convert.ToInt32(row["khu_tro_id"]));
    }

    [TestMethod]
    public async Task ChuTro_TaoTaiKhoanKhachThue_BiTuChoi_DungCongKhaiSelfRegister()
    {
        var resp = await Call(ActionNames.TAI_KHOAN_TAO, new
        {
            tenDangNhap = "012345678999", matKhau = "mk123456", hoTen = "Khach", vaiTro = "KhachThue"
        });
        Assert.IsFalse(resp.Success, "KhachThue chỉ được tạo qua đăng ký công khai");
        Assert.AreEqual(0, await _db.ScalarT<int>("SELECT COUNT(*) FROM tai_khoan WHERE ten_dang_nhap='012345678999'"));
    }

    [TestMethod]
    public async Task ChuTro_CapNhatTaiKhoan_KhongChoTuVaiTroXuanXuongQuanLy()
    {
        var resp = await Call(ActionNames.TAI_KHOAN_CAP_NHAT, new
        {
            taiKhoanId = _managerId,
            vaiTro = "ChuTro",
        });
        Assert.IsFalse(resp.Success, "Không được nâng tài khoản khác lên ChuTro qua cập nhật");
        Assert.AreEqual("LeTan", await _db.ScalarT<string>("SELECT vai_tro FROM tai_khoan WHERE id=@id", new { id = _managerId }));
    }

    [TestMethod]
    public async Task ChuTro_CapNhatTaiKhoan_KhongChoGiuKhuNgoaiVaiTroQuanLy()
    {
        var resp = await Call(ActionNames.TAI_KHOAN_CAP_NHAT, new
        {
            taiKhoanId = _managerId,
            vaiTro = "LeTan",
            khuTroId = _siteId,
        });
        Assert.IsFalse(resp.Success);
        Assert.AreEqual("LeTan", await _db.ScalarT<string>("SELECT vai_tro FROM tai_khoan WHERE id=@id", new { id = _managerId }));
    }
}
