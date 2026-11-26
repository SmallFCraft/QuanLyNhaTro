using QuanLyTro.Shared.Models;
using QuanLyTro.Shared.Protocol;

namespace QuanLyTro.Tests;

[TestClass]
public class PermissionMatrixTests
{
    [TestMethod]
    public void DenyByDefault_UnknownAction_IsDenied()
    {
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.QuanLy, "KHONG_TON_TAI"));
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.ChuTro, "KHONG_TON_TAI"));
    }

    [TestMethod]
    public void QuanLy_HasNoAccountAdmin_NoPermissionAdmin_NoRevenue()
    {
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.QuanLy, ActionNames.TAI_KHOAN_KHOA));
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.QuanLy, ActionNames.TAI_KHOAN_TAO));
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.QuanLy, ActionNames.PHAN_QUYEN_CAP_NHAT_VAI_TRO));
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.QuanLy, ActionNames.CAU_HINH_CAP_NHAT));
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.QuanLy, "THONG_KE_DOANH_THU"));
    }

    [TestMethod]
    public void ChuTro_HasEveryRegisteredAction()
    {
        foreach (var action in DefaultPermissions.AllActionNames)
            Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.ChuTro, action), action);
    }

    [TestMethod]
    public void CongAn_ReadOnly_PhongOnly()
    {
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.CongAn, ActionNames.PHONG_LAY_TAT_CA));
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.CongAn, ActionNames.PHONG_THEM) == false);
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.CongAn, ActionNames.KHACH_THUE_THEM) == false);
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.CongAn, ActionNames.KHU_TRO_THEM) == false);
    }

    [TestMethod]
    public void KhachThue_OnlyOwnProfileActions()
    {
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.KhachThue, ActionNames.KHACH_THUE_HO_SO_CUA_TOI));
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.KhachThue, ActionNames.KHACH_THUE_THEM));
        Assert.IsFalse(DefaultPermissions.Allowed(VaiTroNguoiDung.KhachThue, ActionNames.PHONG_THEM));
    }

    [TestMethod]
    public void RoleCallsInShells_ArePermittedByDefault()
    {
        // QuanLy reset password is exposed by its JS shell; KhachThue loads account payment config for QR.
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.QuanLy, ActionNames.TAI_KHOAN_DAT_LAI_MAT_KHAU));
        Assert.IsTrue(DefaultPermissions.Allowed(VaiTroNguoiDung.KhachThue, ActionNames.CAU_HINH_LAY));
    }
}
