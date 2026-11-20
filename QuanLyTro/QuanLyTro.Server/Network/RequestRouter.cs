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

    private static readonly HashSet<string> PublicActions = new(StringComparer.Ordinal)
    { ActionNames.DANG_NHAP, ActionNames.KHACH_THUE_DANG_KY };

    public RequestRouter(Database db, SessionStore sessions)
    {
        _db = db;
        _sessions = sessions;
        _accounts = new AccountService(db, sessions);
        var siteRepo = new Repositories.SiteRepository(db);
        var roomRepo = new Repositories.RoomRepository(db);
        _sites = new SiteService(siteRepo, roomRepo, sessions);
        _rooms = new RoomService(roomRepo, siteRepo);
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
                // M2 người thuê handlers lắp ở Task 8
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
