using QuanLyTro.Server.Data;
using QuanLyTro.Server.Security;
using QuanLyTro.Server.Services;
using QuanLyTro.Shared.Models;
using QuanLyTro.Shared.Protocol;

namespace QuanLyTro.Server.Network;

/// <summary>
/// Router RBAC deny-by-default: action lạ → từ chối chung; action có quyền → dispatch service.
/// Kiểm tra quyền TRƯỚC khi chạm service.
/// </summary>
public sealed class RequestRouter
{
    private readonly Database _db;
    private readonly SessionStore _sessions;
    private readonly AccountService _accounts;
    private readonly SiteService _sites;
    private readonly RoomService _rooms;
    private readonly TenantService _tenants;
    private readonly HopDongService _hopDongs;
    private readonly DienNuocService _dienNuoc;
    private readonly HoaDonService _hoaDon;
    private readonly DeXuatService _deXuat;
    private readonly SuCoService _suCo;
    private readonly BaoCaoService _baoCao;

    private static readonly HashSet<string> PublicActions = new(StringComparer.Ordinal)
    { ActionNames.DANG_NHAP, ActionNames.KHACH_THUE_DANG_KY };

    public RequestRouter(Database db, SessionStore sessions,
        Repositories.SiteRepository? siteRepo = null,
        Repositories.RoomRepository? roomRepo = null,
        Repositories.TenantRepository? tenantRepo = null)
    {
        _db = db;
        _sessions = sessions;
        _accounts = new AccountService(db, sessions);
        siteRepo ??= new Repositories.SiteRepository(db);
        roomRepo ??= new Repositories.RoomRepository(db);
        tenantRepo ??= new Repositories.TenantRepository(db);
        _sites = new SiteService(siteRepo, roomRepo, sessions);
        _rooms = new RoomService(roomRepo, siteRepo);
        _tenants = new TenantService(tenantRepo, roomRepo, siteRepo);
        _hopDongs = new HopDongService(new Repositories.HopDongRepository(db), roomRepo, tenantRepo);
        var dienNuocRepo = new Repositories.DienNuocRepository(db);
        var hoaDonRepo = new Repositories.HoaDonRepository(db);
        _dienNuoc = new DienNuocService(dienNuocRepo, roomRepo);
        _hoaDon = new HoaDonService(hoaDonRepo, dienNuocRepo, roomRepo);
        // M4b
        var deXuatRepo = new Repositories.DeXuatRepository(db);
        var suCoRepo = new Repositories.SuCoRepository(db);
        _deXuat = new DeXuatService(deXuatRepo, roomRepo, tenantRepo);
        _suCo = new SuCoService(suCoRepo, roomRepo, tenantRepo);
        _baoCao = new BaoCaoService(db);
    }

    public async Task<ResponsePacket> HandleAsync(RequestPacket packet, ClientContext ctx)
    {
        try
        {
            // 1. Action phải có handler đăng ký
            if (!IsRegistered(packet.Action))
                return Deny(packet.RequestId);

            // 2. Phiên: public action không cần token, còn lại bắt buộc token hợp lệ
            SessionInfo? session = null;
            if (!PublicActions.Contains(packet.Action))
            {
                if (string.IsNullOrEmpty(packet.Token))
                    return Deny(packet.RequestId);
                session = await _sessions.GetAsync(packet.Token);
                if (session is null)
                    return Deny(packet.RequestId);
            }

            // 3. RBAC: vai trò phải được phép với action (default permissions, chạy mỗi request)
            if (session is not null &&
                !DefaultPermissions.Allowed(session.VaiTro, packet.Action))
                return Deny(packet.RequestId);

            // 4. Dispatch
            var data = packet.Action switch
            {
                ActionNames.DANG_NHAP => await _accounts.LoginAsync(packet.Data),
                ActionNames.DANG_XUAT => await _accounts.LogoutAsync(session!, packet.Token!),
                ActionNames.DOI_MAT_KHAU => await _accounts.ChangePasswordAsync(session!, packet.Data),
                ActionNames.TAI_KHOAN_LAY_TAT_CA => await _accounts.ListAsync(session!, packet.Data),
                ActionNames.TAI_KHOAN_TAO => await _accounts.CreateAsync(session!, packet.Data),
                ActionNames.TAI_KHOAN_CAP_NHAT => await _accounts.UpdateAsync(session!, packet.Data),
                ActionNames.TAI_KHOAN_KHOA => await _accounts.LockAsync(session!, packet.Data),
                ActionNames.TAI_KHOAN_MO_KHOA => await _accounts.UnlockAsync(session!, packet.Data),
                ActionNames.TAI_KHOAN_DAT_LAI_MAT_KHAU => await _accounts.ResetPasswordAsync(session!, packet.Data),
                ActionNames.TAI_KHOAN_XOA => await _accounts.DeleteAsync(session!, packet.Data),
                ActionNames.PHAN_QUYEN_LAY_MA_TRAN => await _accounts.GetPermissionMatrixAsync(session!),
                ActionNames.PHAN_QUYEN_CAP_NHAT_VAI_TRO => await _accounts.UpdateRolePermissionsAsync(session!, packet.Data),
                ActionNames.CAU_HINH_LAY => await _accounts.GetConfigAsync(session!, withSecret: false),
                ActionNames.CAU_HINH_DAY_DU => await _accounts.GetConfigAsync(session!, withSecret: true),
                ActionNames.CAU_HINH_CAP_NHAT => await _accounts.UpdateConfigAsync(session!, packet.Data),
                // M2: khu trọ
                ActionNames.KHU_TRO_LAY_TAT_CA => await _sites.ListAsync(session!, packet.Data),
                ActionNames.KHU_TRO_THEM => await _sites.CreateAsync(session!, packet.Data),
                ActionNames.KHU_TRO_CAP_NHAT => await _sites.UpdateAsync(session!, packet.Data),
                ActionNames.KHU_TRO_PHAN_CONG_QUAN_LY => await _sites.AssignManagerAsync(session!, packet.Data),
                ActionNames.KHU_TRO_XOA => await _sites.DeleteAsync(session!, packet.Data),
                ActionNames.KHU_TRO_NGUNG_HOAT_DONG => await _sites.SuspendAsync(session!, packet.Data),
                // M2: phòng
                ActionNames.PHONG_LAY_TAT_CA => await _rooms.ListAsync(session!, packet.Data),
                ActionNames.PHONG_LAY_CHI_TIET => await _rooms.GetDetailAsync(session!, packet.Data),
                ActionNames.PHONG_THEM => await _rooms.CreateAsync(session!, packet.Data),
                ActionNames.PHONG_CAP_NHAT => await _rooms.UpdateAsync(session!, packet.Data),
                ActionNames.PHONG_XOA => await _rooms.DeleteAsync(session!, packet.Data),
                // M2: người thuê
                ActionNames.KHACH_THUE_DANG_KY => await _tenants.RegisterAsync(packet.Data),
                ActionNames.KHACH_THUE_THEM => await _tenants.AddAsync(session!, packet.Data),
                ActionNames.KHACH_THUE_CAP_NHAT => await _tenants.UpdateAsync(session!, packet.Data),
                ActionNames.KHACH_THUE_CAP_NHAT_TAM_TRU => await _tenants.UpdateTamTruAsync(session!, packet.Data),
                ActionNames.KHACH_THUE_CHUYEN_PHONG => await _tenants.MoveRoomAsync(session!, packet.Data),
                ActionNames.KHACH_THUE_TRA_PHONG => await _tenants.CheckOutAsync(session!, packet.Data),

                // M3: hop dong + qr/pin nhan phong
                ActionNames.HopDongTao => await _hopDongs.CreateAsync(session!, packet.Data),
                ActionNames.HopDongGiaHan => await _hopDongs.GiaHanAsync(session!, packet.Data),
                ActionNames.HopDongChamDut => await _hopDongs.ChamDutAsync(session!, packet.Data),
                ActionNames.HopDongLayTatCa => await _hopDongs.GetAllAsync(session!, packet.Data),
                ActionNames.HopDongSinhQr => await _hopDongs.SinhQrAsync(session!, packet.Data),
                ActionNames.PhuLucLay => await _hopDongs.PhuLucLayAsync(session!, packet.Data),
                ActionNames.PhuLucCapNhat => await _hopDongs.PhuLucCapNhatAsync(session!, packet.Data),
                ActionNames.KhachThueNhanPhongQr => await _hopDongs.NhanPhongQrAsync(session!, packet.Data),
                ActionNames.KHACH_THUE_XOA => await _tenants.DeleteAsync(session!, packet.Data),
                ActionNames.KHACH_THUE_TIM_KIEM => await _tenants.SearchAsync(session!, packet.Data),
                ActionNames.KHACH_THUE_HO_SO_CUA_TOI => await _tenants.GetMyProfileAsync(session!),
                ActionNames.KHACH_THUE_CAP_NHAT_LIEN_HE => await _tenants.UpdateLienHeAsync(session!, packet.Data),
                ActionNames.KHACH_THUE_LAY_THEO_PHONG => await _tenants.GetByPhongAsync(session!, packet.Data),
                // M4a: điện nước + hóa đơn + đối soát
                ActionNames.DIEN_NUOC_LAY_KY_TRUOC => await _dienNuoc.LayKyTruocAsync(session!, packet.Data),
                ActionNames.DIEN_NUOC_GHI_SO => await _dienNuoc.GhiSoAsync(session!, packet.Data),
                ActionNames.HOA_DON_TAO => await _hoaDon.TaoAsync(session!, packet.Data),
                ActionNames.HOA_DON_LAY_TAT_CA => await _hoaDon.LayTatCaAsync(session!, packet.Data),
                ActionNames.HOA_DON_THANH_TOAN => await _hoaDon.ThanhToanAsync(session!, packet.Data),
                ActionNames.HOA_DON_CUA_TOI => await _hoaDon.CuaToiAsync(session!, packet.Data),
                ActionNames.DOI_SOAT_CHAY => await _hoaDon.DoiSoatAsync(session!, packet.Data),
                // M4b: đề xuất + sự cố + báo cáo
                ActionNames.DeXuatTao => await _deXuat.TaoAsync(session!, packet.Data),
                ActionNames.DeXuatLayTatCa => await _deXuat.LayTatCaAsync(session!),
                ActionNames.DeXuatDuyet => await _deXuat.DuyetAsync(session!, packet.Data),
                ActionNames.DeXuatTuChoi => await _deXuat.TuChoiAsync(session!, packet.Data),
                ActionNames.SuCoTao => await _suCo.TaoAsync(session!, packet.Data),
                ActionNames.SuCoLayTheoKhu => await _suCo.LayTheoKhuAsync(session!, packet.Data),
                ActionNames.SuCoCapNhatTrangThai => await _suCo.CapNhatTrangThaiAsync(session!, packet.Data),
                ActionNames.BaoCaoTongQuan => await _baoCao.TongQuanAsync(session!, packet.Data),
                _ => throw new NotFoundOrDeniedException(),
            };
            return ResponsePacket.Ok(packet.RequestId, data);
        }
        catch (NotFoundOrDeniedException ex)
        {
            return ResponsePacket.Fail(packet.RequestId, ex.Message);
        }
        catch (AuthorizationException ex)
        {
            return ResponsePacket.Fail(packet.RequestId, ex.Message);
        }
        catch (AuthenticationFailedException ex)
        {
            return ResponsePacket.Fail(packet.RequestId, ex.Message);
        }
        catch (AccountLockedException ex)
        {
            return ResponsePacket.Fail(packet.RequestId, ex.Message);
        }
        catch (ValidationException ex)
        {
            return ResponsePacket.Fail(packet.RequestId, ex.Message);
        }
        catch (BusinessRuleException ex)
        {
            return ResponsePacket.Fail(packet.RequestId, ex.Message);
        }
        catch (Exception)
        {
            return ResponsePacket.Fail(packet.RequestId, "Lỗi hệ thống. Vui lòng thử lại sau.");
        }
    }

    private static bool IsRegistered(string action) =>
        DefaultPermissions.AllActionNames.Contains(action);

    private static ResponsePacket Deny(string requestId) =>
        ResponsePacket.Fail(requestId, new NotFoundOrDeniedException().Message);
}
