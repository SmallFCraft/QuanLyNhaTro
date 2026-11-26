// Màn hình Phân quyền động (chỉ Chủ trọ được phép truy cập).
let _permsData = null;
let _currentRole = 'QuanLy';
let _permsDraft = new Set();

const ROLE_LABELS = {
  ChuTro: { name: 'Chủ trọ', icon: 'fa-user-cog', desc: 'Quản trị toàn hệ thống' },
  QuanLy: { name: 'Quản lý khu trọ', icon: 'fa-user-tie', desc: 'Nhân viên phụ trách vận hành' },
  LeTan: { name: 'Lễ tân', icon: 'fa-id-card', desc: 'Duyệt lịch, gán nhân viên' },
  NhanVien: { name: 'Nhân viên', icon: 'fa-hard-hat', desc: 'Thực hiện công việc được giao' },
  CongAn:  { name: 'Công an phường', icon: 'fa-user-shield', desc: 'Cán bộ kiểm tra cư trú' },
  KhachThue:  { name: 'Khách thuê phòng', icon: 'fa-user', desc: 'Người thuê xem hóa đơn' }
};

async function loadPerms() {
  try {
    const res = await window.bridge.call('PHAN_QUYEN_LAY_MA_TRAN', {});
    if (!res) return;
    _permsData = res;
    renderPermsRoleSelector();
    selectPermsRole(_currentRole);
  } catch (err) {
    toast(err.message, 'err');
  }
}

function renderPermsRoleSelector() {
  const c = document.getElementById('permsRoles');
  if (!c || !_permsData) return;
  c.innerHTML = Object.entries(ROLE_LABELS).map(([vaiTro, meta]) => {
    const on = vaiTro === _currentRole ? ' active' : '';
    return `<button type="button" class="perms-vai_tro-btn${on}" onclick="selectPermsRole('${vaiTro}')">
      <i class="fas ${meta.icon}"></i>
      <span class="vai_tro-title">${meta.name}</span>
      <span class="vai_tro-desc">${meta.desc}</span>
    </button>`;
  }).join('');
}

function actionsForRole(vaiTro) {
  const effective = _permsData?.hieuLuc || [];
  const active = effective.filter(x => (x.vaiTro || x.VaiTro) === vaiTro).map(x => x.hanhDong || x.HanhDong).filter(Boolean);
  if ((_permsData?.vaiTroTuyChinh || []).includes(vaiTro)) return active;
  if (active.length) return active;
  const defaults = _permsData?.macDinh || [];
  return defaults.find(x => (x.vaiTro || x.VaiTro) === vaiTro)?.hanhDong || [];
}

function selectPermsRole(vaiTro) {
  _currentRole = vaiTro;
  renderPermsRoleSelector();
  _permsDraft = new Set(actionsForRole(vaiTro));
  renderPermsCheckboxes();
}

function togglePermAction(hanhDong, checked) {
  if (checked) {
    _permsDraft.add(hanhDong);
  } else {
    _permsDraft.delete(hanhDong);
  }
}

function renderPermsCheckboxes() {
  const container = document.getElementById('permsGroups');
  if (!container || !_permsData) return;

  const catalog = _permsData.danhSachHanhDong || _permsData.DanhSachHanhDong || [];
  const defaults = _permsData.macDinh || [];
  const groups = {};
  catalog.forEach(item => {
    const nhom = item.nhom || item.Nhom || item.group || 'Khác';
    if (!groups[nhom]) groups[nhom] = [];
    groups[nhom].push(item);
  });
  defaults.forEach(role => (role.hanhDong || []).forEach(code => {
    if (!groups['Quyền hệ thống']) groups['Quyền hệ thống'] = [];
    if (!groups['Quyền hệ thống'].some(item => (item.hanhDong || item.HanhDong) === code))
      groups['Quyền hệ thống'].push({ hanhDong: code, moTa: code });
  }));

  const html = Object.entries(groups).map(([groupName, items]) => {
    const itemsHtml = items.map(item => {
      const code = item.hanhDong || item.HanhDong || item.action || '';
      const desc = item.moTa || item.MoTa || item.description || code;
      const isChecked = _permsDraft.has(code) ? 'checked' : '';
      return `
        <label class="perm-item">
          <input type="checkbox" ${isChecked} onchange="togglePermAction(this.dataset.code, this.checked)" data-code="${esc(code)}">
          <div class="perm-info">
            <span class="perm-desc">${esc(desc)}</span>
            <code class="perm-hanh_dong">${esc(code)}</code>
          </div>
        </label>
      `;
    }).join('');

    return `
      <div class="perm-group-card">
        <div class="perm-group-header">
          <i class="fas fa-folder-open"></i> ${esc(groupName)}
          <span class="perm-group-count">${items.length} quyền</span>
        </div>
        <div class="perm-items-grid">
          ${itemsHtml}
        </div>
      </div>
    `;
  }).join('');

  container.innerHTML = html;
}

function resetPermsDraft() {
  if (!_permsData) return;
  selectPermsRole(_currentRole);
  toast('Đã khôi phục trạng thái ban đầu.', 'info');
}

async function saveRolePermissions() {
  if (!_permsData) return;
  const currentRole = _currentRole;
  const effective = _permsData.hieuLuc || [];
  const rolePermissions = effective.filter(x => x.vaiTro === currentRole).map(x => x.hanhDong);
  const current = new Set(rolePermissions.length || (_permsData.vaiTroTuyChinh || []).includes(currentRole)
    ? rolePermissions
    : _permsData.macDinh?.find(x => x.vaiTro === currentRole)?.hanhDong || []);
  const thuHoi = [...current].filter(x => !_permsDraft.has(x));
  const capQuyen = [..._permsDraft].filter(x => !current.has(x));
  try {
    const res = await window.bridge.call('PHAN_QUYEN_CAP_NHAT_VAI_TRO', {
      VaiTro: currentRole,
      ThuHoi: thuHoi,
      CapQuyen: capQuyen
    });
    _permsData.hieuLuc = effective.filter(x => !(x.vaiTro === currentRole && thuHoi.includes(x.hanhDong)))
      .concat(capQuyen.map(hanhDong => ({ vaiTro: currentRole, hanhDong })));
    _permsData.vaiTroTuyChinh = [...new Set([...(_permsData.vaiTroTuyChinh || []), currentRole])];
    toast(res?.message || `Đã cập nhật phân quyền cho vai trò ${ROLE_LABELS[currentRole]?.name || currentRole}!`, 'ok');
  } catch (err) {
    toast(err.message, 'err');
  }
}
