using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuanLyTro.Server.Data;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Models;
using QuanLyTro.Tests.TestHelpers;

namespace QuanLyTro.Tests;

[TestClass]
public class AuthTests
{
    private Database _db = null!;
    private SessionStore _sessions = null!;
    private FakeTimeProvider _clock = null!;
    private AuthenticationService _auth = null!;

    [TestInitialize]
    public async Task Setup()
    {
        await TestDb.ResetAsync();
        _db = TestDb.Create();
        await new QuanLyTro.Server.Data.SchemaInitializer(_db).InitializeAsync();
        await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro, khu_tro_id, chuyen_mon) " +
            "VALUES ('chutro', @h, 'Chu Tro', 'ChuTro', NULL, NULL)",
            new { h = PasswordHasher.Hash("chutro") });
        await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro, khu_tro_id, chuyen_mon) " +
            "VALUES ('nv1', @h, 'NV 1', 'NhanVien', NULL, 'DonPhong')",
            new { h = PasswordHasher.Hash("nv1") });
        _sessions = new SessionStore();
        _clock = new FakeTimeProvider();
        _auth = new AuthenticationService(_db, _sessions, _clock);
    }

    [TestMethod]
    public async Task Login_Ok_ReturnsSessionWithRoleAndChuyenMon()
    {
        var s = await _auth.LoginAsync("chutro", "chutro");
        Assert.AreEqual(VaiTroNguoiDung.ChuTro, s.VaiTro);
        Assert.IsNull(s.KhuTroId);

        var s2 = await _auth.LoginAsync("nv1", "nv1");
        Assert.AreEqual(VaiTroNguoiDung.NhanVien, s2.VaiTro);
        Assert.AreEqual(ChuyenMonNhanVien.DonPhong, s2.ChuyenMon);
    }

    [TestMethod]
    public async Task Login_WrongPassword_ThrowsGenericMessage_SameForUnknownUser()
    {
        var ex1 = await Assert.ThrowsExceptionAsync<AuthenticationFailedException>(
            () => _auth.LoginAsync("chutro", "sai"));
        var ex2 = await Assert.ThrowsExceptionAsync<AuthenticationFailedException>(
            () => _auth.LoginAsync("khong_ton_tai", "sai"));
        StringAssert.Contains(ex1.Message, "khong dung");
        Assert.AreEqual(ex1.Message, ex2.Message);
    }

    [TestMethod]
    public async Task Login_FiveFailures_LocksForOneMinute_ThenUnlocks()
    {
        for (var i = 0; i < 5; i++)
            await Assert.ThrowsExceptionAsync<AuthenticationFailedException>(
                () => _auth.LoginAsync("chutro", "sai"));

        await Assert.ThrowsExceptionAsync<AccountLockedException>(
            () => _auth.LoginAsync("chutro", "chutro")); // đúng mật khẩu vẫn bị chặn khi khóa

        _clock.Advance(TimeSpan.FromMinutes(1).Add(TimeSpan.FromSeconds(1)));
        var s = await _auth.LoginAsync("chutro", "chutro"); // hết khóa → vào được
        Assert.AreEqual(VaiTroNguoiDung.ChuTro, s.VaiTro);
    }

    [TestMethod]
    public async Task Login_Success_ResetsFailureCounter()
    {
        for (var i = 0; i < 3; i++)
            await Assert.ThrowsExceptionAsync<AuthenticationFailedException>(
                () => _auth.LoginAsync("nv1", "sai"));
        await _auth.LoginAsync("nv1", "nv1"); // thành công → reset
        for (var i = 0; i < 4; i++)
            await Assert.ThrowsExceptionAsync<AuthenticationFailedException>(
                () => _auth.LoginAsync("nv1", "sai"));
        var s = await _auth.LoginAsync("nv1", "nv1"); // lần thứ 5 sau reset vẫn chưa khóa
        Assert.AreEqual(VaiTroNguoiDung.NhanVien, s.VaiTro);
    }


    [TestMethod]
    public async Task Probe_NullColumns_FromDb()
    {
        var rows = await _db.QueryAsync("SELECT khu_tro_id, chuyen_mon FROM tai_khoan WHERE ten_dang_nhap='chutro'");
        var khu = rows[0]["khu_tro_id"];
        Assert.IsTrue(khu is null || khu is DBNull, $"khu_tro_id type={khu?.GetType().FullName ?? "null"} value={khu}");
    }

    [TestMethod]
    public async Task Logout_TokenInvalidated()
    {
        var s = await _auth.LoginAsync("chutro", "chutro");
        await _auth.LogoutAsync(s.Token);
        Assert.IsNull(await _sessions.GetAsync(s.Token));
    }
}

/// <summary>TimeProvider giả để test thời gian khóa không chờ thật.</summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan t) => _utcNow += t;
}
