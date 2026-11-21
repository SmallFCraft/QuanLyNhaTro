using System.Text.Json;
using QuanLyTro.Server.Data;
using QuanLyTro.Server.Network;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Models;
using QuanLyTro.Shared.Protocol;
using QuanLyTro.Tests.TestHelpers;

namespace QuanLyTro.Tests;

/// <summary>
/// M3: hợp đồng + QR/PIN nhận phòng (BR-19). Flow: ChuTro lập HĐ (ChoNhanPhong, sinh PIN)
/// → KhachThue nhập PIN → HĐ HieuLuc, phòng DaThue, mã bị đánh dấu đã dùng.
/// </summary>
[TestClass]
public class HopDongTests
{
    private Database _db = null!;
    private SessionStore _sessions = null!;
    private RequestRouter _router = null!;
    private AuthenticationService _auth = null!;
    private string _chuTroToken = null!;
    private int _khuId;
    private int _phongTrongId;   // phòng 101 trống
    private int _phongDaThueId;  // phòng 102 đã có khách
    private int _khachId;        // khách đã đăng ký, chưa có phòng

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
        _phongTrongId = await _db.ExecuteAsync(
            "INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da) VALUES (@k, '101', 2500000, 2)",
            new { k = _khuId });
        _phongDaThueId = await _db.ExecuteAsync(
            "INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da) VALUES (@k, '102', 2500000, 2)",
            new { k = _khuId });
        await _db.ExecuteAsync(
            "UPDATE phong SET trang_thai = 'DaThue' WHERE id = @p", new { p = _phongDaThueId });

        // khách thuê đăng ký public, chưa có phòng
        await DangKyKhachAsync("012345678901");
        _khachId = await _db.ScalarT<int>("SELECT id FROM tai_khoan WHERE ten_dang_nhap = '012345678901'");
    }

    private async Task<ResponsePacket> Call(string action, string? token, object? data, string ctx = "c") =>
        await _router.HandleAsync(RequestPacket.Create(action, token, data), new ClientContext(ctx));

    private async Task DangKyKhachAsync(string cccd)
    {
        var resp = await Call(ActionNames.KHACH_THUE_DANG_KY, null, new
        {
            tenDangNhap = cccd, matKhau = "mk123456", hoTen = "Khach " + cccd[^2..],
            cccd, ngaySinh = "2000-01-01", soDienThoai = "0901234567", queQuan = "Quang Nam",
        });
        Assert.IsTrue(resp.Success, "DANG_KY: " + resp.Message);
    }

    private async Task<string> DangNhapKhachAsync(string cccd)
    {
        var s = await _auth.LoginAsync(cccd, "mk123456");
        return s.Token;
    }

    /// <summary>Lập hợp đồng chuẩn cho phòng trống, trả (hopDongId, pin).</summary>
    private async Task<(int Id, string Pin)> TaoHopDongChoPhongTrongAsync()
    {
        var resp = await Call(ActionNames.HopDongTao, _chuTroToken, new
        {
            phongId = _phongTrongId,
            nguoiDaiDienId = _khachId,
            ngayBatDau = "2026-12-01",
            ngayKetThuc = "2027-12-01",
            giaThue = 2500000,
            tienCoc = 5000000,
        });
        Assert.IsTrue(resp.Success, "HOP_DONG_TAO: " + resp.Message);
        var el = JsonDefaults.Deserialize<JsonElement>(JsonDefaults.Serialize(resp.Data!));
        return (el.GetProperty("hopDongId").GetInt32(), el.GetProperty("pin").GetString()!);
    }

    private static int GetInt(ResponsePacket resp, string prop)
    {
        var el = JsonDefaults.Deserialize<JsonElement>(JsonDefaults.Serialize(resp.Data!));
        return el.GetProperty(prop).GetInt32();
    }

    private static string Raw(ResponsePacket r) => JsonDefaults.Serialize(r.Data!);

    [TestMethod]
    public async Task TaoHopDong_PhongTrong_SinhPin_ChoNhanPhong()
    {
        var (id, pin) = await TaoHopDongChoPhongTrongAsync();
        Assert.IsTrue(id > 0);
        StringAssert.Matches(pin, new System.Text.RegularExpressions.Regex(@"^\d{8}$"), "PIN 8 số");

        var tt = await _db.ScalarT<string>("SELECT trang_thai FROM hop_dong WHERE id = @id", new { id });
        Assert.AreEqual("ChoNhanPhong", tt);

        var qr = await _db.ScalarT<string>("SELECT qr_data FROM ma_nhan_phong WHERE hop_dong_id = @id", new { id });
        Assert.AreEqual($"QLT|{id}|{pin}", qr);
    }

    [TestMethod]
    public async Task TaoHopDong_PhongDaThue_BiTuChoi()
    {
        var resp = await Call(ActionNames.HopDongTao, _chuTroToken, new
        {
            phongId = _phongDaThueId, nguoiDaiDienId = _khachId,
            ngayBatDau = "2026-12-01", ngayKetThuc = "2027-12-01", giaThue = 2500000,
        });
        Assert.IsFalse(resp.Success);
        StringAssert.Contains(resp.Message, "trống");
    }

    [TestMethod]
    public async Task TaoHopDong_NgayKetThucTruocBatDau_BiTuChoi()
    {
        var resp = await Call(ActionNames.HopDongTao, _chuTroToken, new
        {
            phongId = _phongTrongId, nguoiDaiDienId = _khachId,
            ngayBatDau = "2027-12-01", ngayKetThuc = "2026-12-01", giaThue = 2500000,
        });
        Assert.IsFalse(resp.Success);
    }

    [TestMethod]
    public async Task KhachThue_KhongDuocTaoHopDong()
    {
        var token = await DangNhapKhachAsync("012345678901");
        var resp = await Call(ActionNames.HopDongTao, token, new
        {
            phongId = _phongTrongId, nguoiDaiDienId = _khachId,
            ngayBatDau = "2026-12-01", ngayKetThuc = "2027-12-01", giaThue = 2500000,
        });
        Assert.IsFalse(resp.Success);
    }

    [TestMethod]
    public async Task NhanPhong_QrPin_FlowDung1La()
    {
        var (id, pin) = await TaoHopDongChoPhongTrongAsync();
        var token = await DangNhapKhachAsync("012345678901");

        // nhận phòng bằng PIN
        var resp = await Call(ActionNames.KhachThueNhanPhongQr, token, new { tokenOrPin = pin });
        Assert.IsTrue(resp.Success, "NHAN_PHONG: " + resp.Message);
        var json = Raw(resp);
        StringAssert.Contains(json, "soPhong");
        StringAssert.Contains(json, "101");
        StringAssert.Contains(json, "hopDongId");

        // hợp đồng HieuLuc, phòng DaThue, khách có phong_id, lịch sử ghi NhanPhong
        Assert.AreEqual("HieuLuc", await _db.ScalarT<string>("SELECT trang_thai FROM hop_dong WHERE id = @id", new { id }));
        Assert.AreEqual("DaThue", await _db.ScalarT<string>("SELECT trang_thai FROM phong WHERE id = @p", new { p = _phongTrongId }));
        Assert.AreEqual(_phongTrongId, await _db.ScalarT<int?>("SELECT phong_id FROM ho_so_khach_thue WHERE tai_khoan_id = @t", new { t = _khachId }));
        Assert.AreEqual(1, await _db.ScalarT<int>(
            "SELECT COUNT(*) FROM lich_su_cu_tru WHERE tai_khoan_khach_id = @t AND loai_bien_dong = 'NhanPhong'", new { t = _khachId }));

        // mã đã dùng — dùng lại phải báo lỗi rõ ràng
        var again = await Call(ActionNames.KhachThueNhanPhongQr, token, new { tokenOrPin = pin });
        Assert.IsFalse(again.Success);
        StringAssert.Contains(again.Message, "sai");
    }

    [TestMethod]
    public async Task NhanPhong_PinSai_BiTuChoi()
    {
        await TaoHopDongChoPhongTrongAsync();
        var token = await DangNhapKhachAsync("012345678901");
        var resp = await Call(ActionNames.KhachThueNhanPhongQr, token, new { tokenOrPin = "99999999" });
        Assert.IsFalse(resp.Success);
    }

    [TestMethod]
    public async Task NhanPhong_MaHetHan_BiTuChoi()
    {
        var (id, _) = await TaoHopDongChoPhongTrongAsync();
        // mã đã hết hạn từ hôm qua
        await _db.ExecuteAsync("UPDATE ma_nhan_phong SET het_han = DATE_SUB(CURDATE(), INTERVAL 1 DAY) WHERE hop_dong_id = @id",
            new { id });
        var token = await DangNhapKhachAsync("012345678901");
        var resp = await Call(ActionNames.KhachThueNhanPhongQr, token, new { tokenOrPin = $"QLT|{id}|12345678" });
        Assert.IsFalse(resp.Success);
    }

    [TestMethod]
    public async Task NhanPhong_TokenQr_NhieuTruongHop()
    {
        var (id, pin) = await TaoHopDongChoPhongTrongAsync();
        var token = await DangNhapKhachAsync("012345678901");
        // nhận bằng qr_data đầy đủ
        var resp = await Call(ActionNames.KhachThueNhanPhongQr, token, new { tokenOrPin = $"QLT|{id}|{pin}" });
        Assert.IsTrue(resp.Success, resp.Message);
    }

    [TestMethod]
    public async Task NhanPhong_KhachVangLai_KhongToken_BiDeny()
    {
        var (_, pin) = await TaoHopDongChoPhongTrongAsync();
        var resp = await Call(ActionNames.KhachThueNhanPhongQr, null, new { tokenOrPin = pin });
        Assert.IsFalse(resp.Success);
    }

    [TestMethod]
    public async Task LayTatCa_ChuTroThayHet_QuanLyChiThayKhuCuaMinh()
    {
        var (_, _) = await TaoHopDongChoPhongTrongAsync();

        // khu B + phòng + hợp đồng riêng
        var khuB = await _db.ExecuteAsync("INSERT INTO khu_tro (ten_khu_tro, dia_chi) VALUES ('Khu B', 'Hoi An')");
        var phongB = await _db.ExecuteAsync(
            "INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da) VALUES (@k, '201', 2000000, 2)", new { k = khuB });
        await DangKyKhachAsync("012345678902");
        var khachB = await _db.ScalarT<int>("SELECT id FROM tai_khoan WHERE ten_dang_nhap = '012345678902'");
        await Call(ActionNames.HopDongTao, _chuTroToken, new
        {
            phongId = phongB, nguoiDaiDienId = khachB,
            ngayBatDau = "2026-12-01", ngayKetThuc = "2027-12-01", giaThue = 2000000,
        });

        // ChuTro thấy cả 2
        var all = await Call(ActionNames.HopDongLayTatCa, _chuTroToken, new { });
        Assert.IsTrue(all.Success, all.Message);
        var jsonAll = Raw(all);
        StringAssert.Contains(jsonAll, "101");
        StringAssert.Contains(jsonAll, "201");
        // camelCase field names
        StringAssert.Contains(jsonAll, "tenNguoiDaiDien");
        StringAssert.Contains(jsonAll, "trangThai");

        // QuanLy gán khu A chỉ thấy khu A
        await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro, khu_tro_id) VALUES ('quanly', @h, 'Quan Ly A', 'QuanLy', @k)",
            new { h = PasswordHasher.Hash("quanly"), k = _khuId });
        var sQl = await _auth.LoginAsync("quanly", "quanly");
        var mine = await Call(ActionNames.HopDongLayTatCa, sQl.Token, new { });
        Assert.IsTrue(mine.Success, mine.Message);
        var jsonMine = Raw(mine);
        StringAssert.Contains(jsonMine, "101");
        Assert.IsFalse(jsonMine.Contains("201"), "QuanLy khu A không được thấy hợp đồng khu B");
    }

    [TestMethod]
    public async Task GiaHan_TaoPhuLuc_CapNhatNgayKetThuc()
    {
        var (id, _) = await TaoHopDongChoPhongTrongAsync();
        // đưa hợp đồng vào hiệu lực bằng cách nhận phòng
        var token = await DangNhapKhachAsync("012345678901");
        await Call(ActionNames.KhachThueNhanPhongQr, token, new { tokenOrPin = (await _db.ScalarT<string>("SELECT pin FROM ma_nhan_phong WHERE hop_dong_id = @id", new { id }))! });

        var resp = await Call(ActionNames.HopDongGiaHan, _chuTroToken, new { hopDongId = id, newEndDate = "2028-06-30" });
        Assert.IsTrue(resp.Success, resp.Message);
        var newEnd = await _db.ScalarT<DateTime>("SELECT ngay_ket_thuc FROM hop_dong WHERE id = @id", new { id });
        Assert.AreEqual(new DateTime(2028, 6, 30), newEnd.Date);
        Assert.AreEqual(1, await _db.ScalarT<int>(
            "SELECT COUNT(*) FROM phu_luc_hop_dong WHERE hop_dong_id = @id AND loai = 'GiaHan'", new { id }));
    }

    [TestMethod]
    public async Task GiaHan_NgayMoiTruocNgayCu_BiTuChoi()
    {
        var (id, _) = await TaoHopDongChoPhongTrongAsync();
        var resp = await Call(ActionNames.HopDongGiaHan, _chuTroToken, new { hopDongId = id, newEndDate = "2026-01-01" });
        Assert.IsFalse(resp.Success);
    }

    [TestMethod]
    public async Task ChamDut_PhongVeTrong_MaConLaiBiVoHieu()
    {
        var (id, pin) = await TaoHopDongChoPhongTrongAsync();
        var resp = await Call(ActionNames.HopDongChamDut, _chuTroToken, new { hopDongId = id, ghiChu = "Khach hoan tra" });
        Assert.IsTrue(resp.Success, resp.Message);
        Assert.AreEqual("ChamDut", await _db.ScalarT<string>("SELECT trang_thai FROM hop_dong WHERE id = @id", new { id }));
        Assert.AreEqual("Trong", await _db.ScalarT<string>("SELECT trang_thai FROM phong WHERE id = @p", new { p = _phongTrongId }));
        Assert.AreEqual(0, await _db.ScalarT<int>(
            "SELECT COUNT(*) FROM ma_nhan_phong WHERE hop_dong_id = @id AND da_su_dung = 0", new { id }));

        // PIN vô hiệu — khách không thể nhận phòng nữa
        var token = await DangNhapKhachAsync("012345678901");
        var nhan = await Call(ActionNames.KhachThueNhanPhongQr, token, new { tokenOrPin = pin });
        Assert.IsFalse(nhan.Success);
    }

    [TestMethod]
    public async Task ChamDut_Lan2_BiTuChoi()
    {
        var (id, _) = await TaoHopDongChoPhongTrongAsync();
        await Call(ActionNames.HopDongChamDut, _chuTroToken, new { hopDongId = id });
        var again = await Call(ActionNames.HopDongChamDut, _chuTroToken, new { hopDongId = id });
        Assert.IsFalse(again.Success);
    }

    [TestMethod]
    public async Task SinhQr_MaCuVoHieu_MaMoiConHan()
    {
        var (id, pinCu) = await TaoHopDongChoPhongTrongAsync();
        var resp = await Call(ActionNames.HopDongSinhQr, _chuTroToken, new { hopDongId = id });
        Assert.IsTrue(resp.Success, resp.Message);
        var el = JsonDefaults.Deserialize<JsonElement>(JsonDefaults.Serialize(resp.Data!));
        var pinMoi = el.GetProperty("maPin").GetString()!;
        StringAssert.Matches(pinMoi, new System.Text.RegularExpressions.Regex(@"^\d{8}$"));

        // mã cũ hết tác dụng
        var token = await DangNhapKhachAsync("012345678901");
        var cu = await Call(ActionNames.KhachThueNhanPhongQr, token, new { tokenOrPin = pinCu });
        Assert.IsFalse(cu.Success);
        // mã mới dùng được
        var moi = await Call(ActionNames.KhachThueNhanPhongQr, token, new { tokenOrPin = pinMoi });
        Assert.IsTrue(moi.Success, resp.Message);
    }

    [TestMethod]
    public async Task PhuLuc_Lay_CapNhat_ThemNguoi()
    {
        var (id, _) = await TaoHopDongChoPhongTrongAsync();

        var lay = await Call(ActionNames.PhuLucLay, _chuTroToken, new { hopDongId = id });
        Assert.IsTrue(lay.Success, lay.Message);
        var json = Raw(lay);
        StringAssert.Contains(json, "soPhong");
        StringAssert.Contains(json, "tenNguoiDaiDien");
        StringAssert.Contains(json, "dsPhuLuc");

        var capNhat = await Call(ActionNames.PhuLucCapNhat, _chuTroToken, new
        {
            hopDongId = id,
            nguoiOCung = new object[]
            {
                new { hoTen = "Nguoi O Chung", cccd = "012345678903", ngaySinh = "1999-05-10", queQuan = "Da Nang", noiLamViec = "UEA", soDienThoai = "0909876543" },
            },
        });
        Assert.IsTrue(capNhat.Success, capNhat.Message);
        Assert.AreEqual(1, await _db.ScalarT<int>(
            "SELECT COUNT(*) FROM phu_luc_hop_dong WHERE hop_dong_id = @id AND loai = 'ThemNguoi'", new { id }));

        // CCCD sai bị từ chối, không ghi rác
        var sai = await Call(ActionNames.PhuLucCapNhat, _chuTroToken, new
        {
            hopDongId = id,
            nguoiOCung = new object[] { new { hoTen = "Sai CCCD", cccd = "123", ngaySinh = "1999-05-10", queQuan = "X" } },
        });
        Assert.IsFalse(sai.Success);
        Assert.AreEqual(1, await _db.ScalarT<int>(
            "SELECT COUNT(*) FROM phu_luc_hop_dong WHERE hop_dong_id = @id AND loai = 'ThemNguoi'", new { id }));
    }

    [TestMethod]
    public async Task Rbac_LeTanVaCongAn_KhongCoActionHopDong()
    {
        await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro) VALUES ('letan', @h, 'Le Tan', 'LeTan')",
            new { h = PasswordHasher.Hash("letan") });
        var s = await _auth.LoginAsync("letan", "letan");
        Assert.IsFalse((await Call(ActionNames.HopDongLayTatCa, s.Token, new { })).Success);
        Assert.IsFalse((await Call(ActionNames.HopDongTao, s.Token, new { })).Success);
        Assert.IsFalse((await Call(ActionNames.KhachThueNhanPhongQr, s.Token, new { tokenOrPin = "12345678" })).Success);
    }

    // --- bổ sung theo review post-merge ---

    [TestMethod]
    public async Task TaoHopDong_PhongCoHopDongChuaKetThuc_BiChan()
    {
        await TaoHopDongChoPhongTrongAsync();
        // phòng vẫn Trong (chưa ai nhận) nhưng đã có hợp đồng chờ — lập lần 2 phải bị chặn
        var resp = await Call(ActionNames.HopDongTao, _chuTroToken, new
        {
            phongId = _phongTrongId, nguoiDaiDienId = _khachId,
            ngayBatDau = "2027-01-01", ngayKetThuc = "2028-01-01", giaThue = 2500000,
        });
        Assert.IsFalse(resp.Success);
        StringAssert.Contains(resp.Message, "chưa kết thúc");
    }

    [TestMethod]
    public async Task QuanLy_ChuaGanKhu_KhongThayHopDongMoikhu()
    {
        var (id, _) = await TaoHopDongChoPhongTrongAsync();
        await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro, khu_tro_id) VALUES ('qlnull', @h, 'QL Chua Gan', 'QuanLy', NULL)",
            new { h = PasswordHasher.Hash("mk123456") });
        var s = await _auth.LoginAsync("qlnull", "mk123456");
        var resp = await Call(ActionNames.HopDongLayTatCa, s.Token, new { });
        Assert.IsFalse(resp.Success, "QuanLy chưa gán khu phải bị từ chối, không được thấy hợp đồng mọi khu");
    }

    [TestMethod]
    public async Task NhanPhong_KhongPhaiChuHopDong_BiChan()
    {
        var (_, pin) = await TaoHopDongChoPhongTrongAsync();
        // khách khác (không phải người đại diện) nắm được PIN cũng không nhận được phòng
        await DangKyKhachAsync("012345678909");
        var tokenKhac = await DangNhapKhachAsync("012345678909");
        var resp = await Call(ActionNames.KhachThueNhanPhongQr, tokenKhac, new { tokenOrPin = pin });
        Assert.IsFalse(resp.Success, "Người không phải chủ hợp đồng không được nhận phòng bằng mã của người khác");
        // phòng vẫn Trong, hợp đồng vẫn chờ
        Assert.AreEqual("Trong", await _db.ScalarT<string>("SELECT trang_thai FROM phong WHERE id = @p", new { p = _phongTrongId }));
    }

    [TestMethod]
    public async Task ChamDut_XoaPhongKhoiHoSo_GhiLichSuTraPhong()
    {
        var (id, pin) = await TaoHopDongChoPhongTrongAsync();
        var token = await DangNhapKhachAsync("012345678901");
        await Call(ActionNames.KhachThueNhanPhongQr, token, new { tokenOrPin = pin });
        Assert.AreEqual(_phongTrongId, await _db.ScalarT<int?>("SELECT phong_id FROM ho_so_khach_thue WHERE tai_khoan_id = @t", new { t = _khachId }));

        await Call(ActionNames.HopDongChamDut, _chuTroToken, new { hopDongId = id, ghiChu = "Tra phong" });

        Assert.IsNull(await _db.ScalarT<int?>("SELECT phong_id FROM ho_so_khach_thue WHERE tai_khoan_id = @t", new { t = _khachId }));
        Assert.AreEqual(1, await _db.ScalarT<int>(
            "SELECT COUNT(*) FROM lich_su_cu_tru WHERE tai_khoan_khach_id = @t AND loai_bien_dong = 'TraPhong'", new { t = _khachId }));
    }

    [TestMethod]
    public async Task NhanPhong_Sai5Lan_BiKhoa1Phut()
    {
        var (id, pin) = await TaoHopDongChoPhongTrongAsync();
        var token = await DangNhapKhachAsync("012345678901");
        for (var i = 0; i < 5; i++)
        {
            var sai = await Call(ActionNames.KhachThueNhanPhongQr, token, new { tokenOrPin = "00000000" });
            Assert.IsFalse(sai.Success);
        }
        // đến mã ĐÚNG cũng bị khóa
        var dung = await Call(ActionNames.KhachThueNhanPhongQr, token, new { tokenOrPin = pin });
        Assert.IsFalse(dung.Success);
        StringAssert.Contains(dung.Message, "quá nhiều lần");
    }

    [TestMethod]
    public async Task PhuLucLay_TraNguoiOCung_KhopClient()
    {
        var (id, _) = await TaoHopDongChoPhongTrongAsync();
        await Call(ActionNames.PhuLucCapNhat, _chuTroToken, new
        {
            hopDongId = id,
            nguoiOCung = new object[] { new { hoTen = "NOC A", cccd = "012345678904", ngaySinh = "1999-05-10", queQuan = "Da Nang", noiLamViec = "UEA", soDienThoai = "0909876543" } },
        });
        var lay = await Call(ActionNames.PhuLucLay, _chuTroToken, new { hopDongId = id });
        Assert.IsTrue(lay.Success, lay.Message);
        var json = Raw(lay);
        StringAssert.Contains(json, "nguoiOCung");
        StringAssert.Contains(json, "NOC A");
        StringAssert.Contains(json, "012345678904");
    }
}
