function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
}

// Sự cố: xem theo khu + cập nhật trạng thái (SU_CO_LAY_THEO_KHU / SU_CO_CAP_NHAT_TRANG_THAI).
let _incidents = [];
let _incidentSites = [];

async function loadIncidents() {
  const [incidents, sites] = await Promise.all([
    window.bridge.call('SU_CO_LAY_THEO_KHU', {}),
    window.bridge.call('KHU_TRO_LAY_TAT_CA', {})
  ]);
  _incidents = incidents || [];
  _incidentSites = sites || [];
  const khuSel = document.getElementById('incident-khu');
  if (khuSel) {
    const current = khuSel.value;
    khuSel.innerHTML = '<option value="">Tất cả khu</option>' +
      _incidentSites.map(s => `<option value="${s.id}">${escapeHtml(s.tenKhuTro)}</option>`).join('');
    khuSel.value = current;
  }
  renderIncidents();
}

function incidentStatusLabel(t) {
  return { Moi: 'Mới', DangXuLy: 'Đang xử lý', DaXuLy: 'Đã xử lý' }[t] || t;
}
function incidentMucDoLabel(m) {
  return { Thap: 'Thấp', TrungBinh: 'Trung bình', Cao: 'Cao', KhanCap: 'Khẩn cấp' }[m] || m;
}

function renderIncidents() {
  const body = document.getElementById('incident-body');
  if (!body) return;
  const khu = document.getElementById('incident-khu')?.value || '';
  const tt = document.getElementById('incident-trangthai')?.value || '';
  const rows = _incidents.filter(i =>
    (!khu || String(i.khuTroId) === khu) && (!tt || i.trangThai === tt));
  if (!rows.length) { body.innerHTML = '<tr><td colspan="7">Không có sự cố.</td></tr>'; return; }
  const mucDoTag = { KhanCap: 'bad', Cao: 'warn', TrungBinh: '', Thap: '' };
  body.innerHTML = rows.map(i => `<tr>
    <td>${escapeHtml(i.tenKhuTro || '')} / ${escapeHtml(i.soPhong || i.phongId)}</td>
    <td><span class="tag ${mucDoTag[i.mucDo] || ''}">${incidentMucDoLabel(i.mucDo)}</span></td>
    <td>${escapeHtml(i.moTa)}</td>
    <td>${escapeHtml(i.tenNguoiBao || '')}</td>
    <td class="mono">${escapeHtml(i.taoLuc)}</td>
    <td>${incidentStatusLabel(i.trangThai)}${i.ketQua ? `<div class="muted">${escapeHtml(i.ketQua)}</div>` : ''}</td>
    <td>${i.trangThai !== 'DaXuLy' ? `<button class="btn btn-outline btn-sm" onclick="updateIncident(${i.id}, '${i.trangThai}')">${i.trangThai === 'Moi' ? 'Tiếp nhận' : 'Hoàn tất'}</button>` : ''}</td>
  </tr>`).join('');
}

async function updateIncident(id, current) {
  const next = current === 'Moi' ? 'DangXuLy' : 'DaXuLy';
  let ketQua;
  if (next === 'DaXuLy') {
    ketQua = prompt('Kết quả xử lý (tùy chọn):');
    if (ketQua === null) return;
  }
  try {
    await window.bridge.call('SU_CO_CAP_NHAT_TRANG_THAI', { suCoId: id, trangThai: next, ketQua: ketQua || undefined });
    toast(next === 'DangXuLy' ? 'Đã tiếp nhận sự cố.' : 'Đã hoàn tất sự cố.', 'ok');
    await loadIncidents();
  } catch (e) { toast('Lỗi: ' + e.message, 'err'); }
}

document.addEventListener('DOMContentLoaded', () => {
  document.getElementById('incident-khu')?.addEventListener('change', renderIncidents);
  document.getElementById('incident-trangthai')?.addEventListener('change', renderIncidents);
});
window.loadIncidents = loadIncidents;
