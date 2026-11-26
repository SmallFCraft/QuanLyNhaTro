// Shell Lễ tân — duyệt / từ chối / gán nhân viên cho lịch kiểm tra phòng.
let lichCache = [];
let nhanVienCache = [];

const LICH_TAG = {
  ChoDuyet:  ['Chờ duyệt', 'pill-amber'],
  DaDuyet:   ['Đã duyệt', 'pill-green'],
  TuChoi:    ['Đã từ chối', 'pill-red'],
  DangLam:   ['Đang làm', 'pill-green'],
  HoanThanh: ['Hoàn thành', 'pill-gray']
};

let _activeTab = 'schedule';

function loadLeTanTab(key) {
  _activeTab = key;
  document.getElementById('pane-schedule').hidden = key !== 'schedule';
  document.getElementById('pane-incidents').hidden = key !== 'incidents';
  document.querySelectorAll('.nav-btn[data-tab]').forEach(b => b.classList.toggle('active', b.dataset.tab === key));
  if (key === 'schedule') { document.getElementById('ptitle').textContent = 'Lịch kiểm tra phòng'; napLich(); }
  else if (key === 'incidents') napSuCo();
}

async function napLich() {
  try {
    lichCache = (await window.bridge.call('LICH_KIEM_TRA_LAY', {})) || [];
    try {
      // Danh sách nhân viên để gán việc: dùng chung LICH_KIEM_TRA_LAY kèm cache phía server;
      // nếu server chưa trả, dropdown rỗng và nút Gán sẽ nhắc chọn.
      nhanVienCache = (await window.bridge.call('LICH_KIEM_TRA_NHAN_VIEN', {})) || [];
    } catch { nhanVienCache = []; }
    renderLich();
  } catch (err) { toast(err.message, 'err'); }
}

function renderLich() {
  const el = document.getElementById('ltab-lich');
  if (!el) return;
  const cnt = document.getElementById('cnt-lich');
  if (cnt) cnt.textContent = lichCache.length;

  const rows = lichCache.map(l => {
    const [label, tag] = LICH_TAG[l.trangThai] || [l.trangThai, 'pill-amber'];
    const tg = new Date(l.thoiGian).toLocaleString('vi-VN');
    const hanhDong = l.trangThai === 'ChoDuyet'
      ? `<button class="btn btn-primary btn-sm" onclick="duyetLich(${l.id})"><i class="fas fa-check"></i> Duyệt</button>
         <button class="btn btn-reject btn-sm" onclick="tuChoiLich(${l.id})"><i class="fas fa-ban"></i> Từ chối</button>`
      : l.trangThai === 'DaDuyet'
        ? `<select class="select" id="nv-${l.id}" style="width:170px">
             <option value="">— Chọn nhân viên —</option>
             ${nhanVienCache.map(n => `<option value="${n.id}">${esc(n.hoTen)} (${esc(n.chuyenMon)})</option>`).join('')}
           </select>
           <button class="btn btn-primary btn-sm" onclick="ganNv(${l.id})"><i class="fas fa-user-plus"></i> Gán</button>`
        : `<span class="pill ${tag}">${l.nhanVienTen ? esc(l.nhanVienTen) : '—'}</span>`;
    return `<tr>
      <td><b>${esc(l.soPhong)}</b></td>
      <td>${esc(l.hoTen)}<br><span class="mono" style="font-size:11px">${esc(l.soDienThoai)}</span></td>
      <td class="mono">${esc(tg)}</td>
      <td><span class="pill ${tag}">${esc(label)}</span></td>
      <td>${l.lyDoTuChoi ? esc(l.lyDoTuChoi) : (l.ketQua ? esc(l.ketQua) : '—')}</td>
      <td style="white-space:nowrap">${hanhDong}</td>
    </tr>`;
  }).join('');

  el.innerHTML = `
    <div class="table-wrap">
    <table class="data-table">
      <thead><tr><th>Phòng</th><th>Khách</th><th>Thời gian</th><th>Trạng thái</th><th>Ghi chú</th><th></th></tr></thead>
      <tbody>${rows || '<tr><td colspan="6" style="text-align:center;color:var(--text-muted)">Chưa có lịch nào</td></tr>'}</tbody>
    </table>
    </div>`;
}

async function duyetLich(id) {
  try {
    await window.bridge.call('LICH_KIEM_TRA_DUYET', { lichId: id });
    toast('Đã duyệt lịch.', 'ok');
    await napLich();
  } catch (err) { toast(err.message, 'err'); }
}

async function tuChoiLich(id) {
  openModal({
    title: 'Từ chối lịch xem phòng',
    fields: [{ name: 'lyDo', label: 'Lý do từ chối', type: 'text', required: true, placeholder: 'VD: phòng đã được giữ, khung giờ bận…' }],
    onSubmit: async v => {
      await window.bridge.call('LICH_KIEM_TRA_TU_CHOI', { lichId: id, lyDo: v.lyDo });
      toast('Đã từ chối lịch.', 'ok');
      await napLich();
    }
  });
}

async function ganNv(id) {
  const nhanVienId = Number(document.getElementById('nv-' + id)?.value);
  if (!nhanVienId) { toast('Vui lòng chọn nhân viên.', 'err'); return; }
  try {
    await window.bridge.call('LICH_KIEM_TRA_GAN_NV', { lichId: id, nhanVienId });
    toast('Đã gán nhân viên.', 'ok');
    await napLich();
  } catch (err) { toast(err.message, 'err'); }
}

async function napSuCo() {
  try {
    const rows = await window.bridge.call('SU_CO_LAY_THEO_KHU', {}) || [];
    const el = document.getElementById('incident-body');
    if (!el) return;
    el.innerHTML = rows.length
      ? `<div class="table-wrap"><table class="data-table"><thead><tr><th>Phòng</th><th>Mức độ</th><th>Mô tả</th><th>Trạng thái</th></tr></thead><tbody>${rows.map(s => `<tr><td>${esc(s.soPhong || '—')}</td><td>${esc(s.mucDo || '')}</td><td>${esc(s.moTa || '')}</td><td>${esc(s.trangThai || '')}</td></tr>`).join('')}</tbody></table></div>`
      : '<p class="helper-text">Chưa có sự cố nào trong khu.</p>';
  } catch (err) { toast(err.message, 'err'); }
}

document.addEventListener('DOMContentLoaded', () => {
  const hoTen = new URLSearchParams(window.location.search).get('u');
  if (hoTen) document.getElementById('pname').textContent = hoTen;
  document.querySelectorAll('.nav-btn[data-tab]').forEach(b =>
    b.addEventListener('click', () => loadLeTanTab(b.dataset.tab)));
  loadLeTanTab('schedule');
  startAutoRefresh(() => loadLeTanTab(_activeTab), 30000);
});
