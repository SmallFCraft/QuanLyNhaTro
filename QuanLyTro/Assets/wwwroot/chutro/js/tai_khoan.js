// Standalone functions and cache for the owner account pane.
let _accounts = [];
let _sites = [];

async function loadAccounts() {
  const [accounts, sites] = await Promise.all([
    window.bridge.call('TAI_KHOAN_LAY_TAT_CA', {}),
    window.bridge.call('KHU_TRO_LAY_TAT_CA', {})
  ]);
  _accounts = accounts || [];
  _sites = sites || [];
  renderAccounts();
}

function renderAccounts() {
  const body = document.getElementById('account-body');
  if (!body) return;
  const q = (document.getElementById('account-search')?.value || '').trim().toLowerCase();
  const rows = _accounts.filter(a => !q || `${a.tenDangNhap} ${a.hoTen} ${a.vaiTro}`.toLowerCase().includes(q));
  body.innerHTML = rows.map(a => `<tr>
    <td>${escapeHtml(a.tenDangNhap)}</td><td>${escapeHtml(a.hoTen)}</td><td>${escapeHtml(a.vaiTro)}</td>
    <td>${a.khuTroId ? escapeHtml(_sites.find(s => s.id === a.khuTroId)?.tenKhuTro || '') : '—'}</td>
    <td>${a.biKhoa ? `Đã khóa${a.lyDoKhoa ? `: ${escapeHtml(a.lyDoKhoa)}` : ''}` : 'Hoạt động'}</td>
    <td><button class="btn btn-outline btn-sm" onclick="editAccount(${a.id})">Sửa</button>
    <button class="btn btn-outline btn-sm" onclick="resetAccountPassword(${a.id})">Đặt lại mật khẩu</button>
    ${a.biKhoa ? `<button class="btn btn-outline btn-sm" onclick="unlockAccount(${a.id})">Mở khóa</button>` : `<button class="btn btn-danger btn-sm" onclick="lockAccount(${a.id})">Khóa</button>`}</td>
  </tr>`).join('') || '<tr><td colspan="6">Không có tài khoản.</td></tr>';
}

async function editAccount(id) {
  const a = _accounts.find(x => x.id === id);
  if (!a) return;
  const roleOptions = ['ChuTro','QuanLy','LeTan','NhanVien','KhachThue','CongAn'].map(x => `<option value="${x}" ${a.vaiTro === x ? 'selected' : ''}>${x}</option>`).join('');
  const siteOptions = `<option value="">Không gán</option>` + _sites.map(s => `<option value="${s.id}" ${a.khuTroId === s.id ? 'selected' : ''}>${escapeHtml(s.tenKhuTro)}</option>`).join('');
  const html = `<label>Tên hiển thị<input id="acc-name" value="${escapeHtml(a.hoTen)}"></label>
    <label>Vai trò<select id="acc-role">${roleOptions}</select></label>
    <label>Khu trọ<select id="acc-site">${siteOptions}</select></label>
    <label><input id="acc-locked" type="checkbox" ${a.biKhoa ? 'checked' : ''}> Tài khoản bị khóa</label>
    <label>Lý do khóa<input id="acc-lock-reason" value="${escapeHtml(a.lyDoKhoa || '')}"></label>
    <button class="btn btn-primary" id="acc-save">Lưu</button>`;
  UIChrome.openModal('Sửa tài khoản', html);
  document.getElementById('acc-save')?.addEventListener('click', async () => {
    try {
      await window.bridge.call('TAI_KHOAN_CAP_NHAT', {
        taiKhoanId: id, hoTen: document.getElementById('acc-name').value,
        vaiTro: document.getElementById('acc-role').value,
        khuTroId: document.getElementById('acc-site').value ? Number(document.getElementById('acc-site').value) : null,
        biKhoa: document.getElementById('acc-locked').checked,
        lyDoKhoa: document.getElementById('acc-lock-reason').value
      });
      UIChrome.closeModal(); UIChrome.showToast('Đã cập nhật tài khoản.'); await loadAccounts();
    } catch (e) { UIChrome.showToast('Lỗi: ' + e.message); }
  });
}

async function lockAccount(id) {
  const a = _accounts.find(x => x.id === id); if (!a) return;
  const lyDo = prompt('Lý do khóa tài khoản:'); if (!lyDo?.trim()) return;
  try { await window.bridge.call('TAI_KHOAN_KHOA', { tenDangNhap: a.tenDangNhap, lyDo: lyDo.trim() }); await loadAccounts(); }
  catch (e) { UIChrome.showToast('Lỗi: ' + e.message); }
}
async function unlockAccount(id) {
  const a = _accounts.find(x => x.id === id); if (!a) return;
  try { await window.bridge.call('TAI_KHOAN_MO_KHOA', { tenDangNhap: a.tenDangNhap }); await loadAccounts(); }
  catch (e) { UIChrome.showToast('Lỗi: ' + e.message); }
}
async function resetAccountPassword(id) {
  const a = _accounts.find(x => x.id === id); if (!a) return;
  const matKhauMoi = prompt(`Mật khẩu mới cho ${a.tenDangNhap} (ít nhất 6 ký tự):`); if (!matKhauMoi) return;
  try { await window.bridge.call('TAI_KHOAN_DAT_LAI_MAT_KHAU', { tenDangNhap: a.tenDangNhap, matKhauMoi }); UIChrome.showToast('Đã đặt lại mật khẩu.'); }
  catch (e) { UIChrome.showToast('Lỗi: ' + e.message); }
}

document.addEventListener('DOMContentLoaded', () => document.getElementById('account-search')?.addEventListener('input', renderAccounts));
window.loadAccounts = loadAccounts;
