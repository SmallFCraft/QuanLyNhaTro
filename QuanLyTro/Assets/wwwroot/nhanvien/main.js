// Shell Nhân viên — xem việc của mình, nhận việc, hoàn thành (ghi kết quả).
let viecCache = [];

const VIEC_TAG = {
  DaDuyet:   ['Mới', 'pill-amber'],
  DangLam:   ['Đang làm', 'pill-green'],
  HoanThanh: ['Hoàn thành', 'pill-gray'],
  TuChoi:    ['Đã hủy', 'pill-red']
};

let _activeTab = 'tasks';

function loadNhanVienTab(key) {
  _activeTab = key;
  document.getElementById('pane-tasks').hidden = key !== 'tasks';
  document.getElementById('pane-incidents').hidden = key !== 'incidents';
  document.querySelectorAll('.nav-btn[data-tab]').forEach(b => b.classList.toggle('active', b.dataset.tab === key));
  if (key === 'tasks') { document.getElementById('ptitle').textContent = 'Việc được giao'; napViec(); }
  else if (key === 'incidents') napSuCo();
}

async function napViec() {
  try {
    viecCache = (await window.bridge.call('LICH_KIEM_TRA_CUA_TOI', {})) || [];
    renderViec();
  } catch (err) { toast(err.message, 'err'); }
}

function renderViec() {
  const el = document.getElementById('ntab-viec');
  if (!el) return;
  const cnt = document.getElementById('cnt-viec');
  if (cnt) {
    cnt.textContent = viecCache.filter(v => v.trangThai !== 'HoanThanh' && v.trangThai !== 'TuChoi').length;
  }

  const rows = viecCache.map(v => {
    const [label, tag] = VIEC_TAG[v.trangThai] || [v.trangThai, 'pill-amber'];
    const tg = new Date(v.thoiGian).toLocaleString('vi-VN');
    const hanhDong = v.trangThai === 'DaDuyet'
      ? `<button class="btn btn-primary btn-sm" onclick="nhanViec(${v.id})"><i class="fas fa-hand-pointer"></i> Nhận việc</button>`
      : v.trangThai === 'DangLam'
        ? `<button class="btn btn-primary btn-sm" onclick="hoanThanhViec(${v.id})"><i class="fas fa-check-double"></i> Hoàn thành</button>`
        : `<span class="pill ${tag}">${v.ketQua ? esc(v.ketQua) : '—'}</span>`;
    return `<tr>
      <td><b>${esc(v.soPhong)}</b></td>
      <td>${esc(v.hoTen)}<br><span class="mono" style="font-size:11px">${esc(v.soDienThoai)}</span></td>
      <td class="mono">${esc(tg)}</td>
      <td><span class="pill ${tag}">${esc(label)}</span></td>
      <td>${v.lyDoTuChoi ? esc(v.lyDoTuChoi) : '—'}</td>
      <td style="white-space:nowrap">${hanhDong}</td>
    </tr>`;
  }).join('');

  el.innerHTML = `
    <div class="table-wrap">
    <table class="data-table">
      <thead><tr><th>Phòng</th><th>Khách</th><th>Thời gian</th><th>Trạng thái</th><th>Ghi chú</th><th></th></tr></thead>
      <tbody>${rows || '<tr><td colspan="6" style="text-align:center;color:var(--text-muted)">Chưa có việc nào</td></tr>'}</tbody>
    </table>
    </div>`;
}

async function nhanViec(id) {
  try {
    await window.bridge.call('LICH_KIEM_TRA_NHAN_VIEC', { lichId: id });
    toast('Đã nhận việc.', 'ok');
    await napViec();
  } catch (err) { toast(err.message, 'err'); }
}

async function hoanThanhViec(id) {
  openModal({
    title: 'Hoàn thành kiểm tra phòng',
    fields: [{
      name: 'ketQua',
      label: 'Kết quả (đã dọn phòng / đã thu tiền / hỏng hóc kỹ thuật cần sửa)',
      type: 'text',
      required: true,
      placeholder: 'VD: Đã dọn phòng, khách dự kiến thuê từ tháng sau.'
    }],
    onSubmit: async v => {
      await window.bridge.call('LICH_KIEM_TRA_HOAN_THANH', { lichId: id, ketQua: v.ketQua });
      toast('Đã ghi nhận hoàn thành.', 'ok');
      await napViec();
    }
  });
}

async function napSuCo() {
  try {
    const rows = await window.bridge.call('SU_CO_LAY_THEO_KHU', {}) || [];
    const el = document.getElementById('incident-body');
    if (!el) return;
    el.innerHTML = rows.length
      ? `<div class="table-wrap"><table class="data-table"><thead><tr><th>Phòng</th><th>Mức độ</th><th>Mô tả</th><th>Trạng thái</th></tr></thead><tbody>${rows.map(s => `<tr><td>${esc(s.soPhong || '—')}</td><td>${esc(s.mucDo || '')}</td><td>${esc(s.moTa || '')}</td><td>${esc(s.trangThai || '')}</td></tr>`).join('')}</tbody></table></div>`
      : '<p class="helper-text">Bạn chưa gửi sự cố nào.</p>';
  } catch (err) { toast(err.message, 'err'); }
}

function openStaffIssueModal() {
  openModal({
    title: 'Báo sự cố khu trọ',
    fields: [
      { name: 'mucDo', label: 'Mức độ', type: 'select', options: [{ value: 'Thap', label: 'Thấp' }, { value: 'TrungBinh', label: 'Trung bình' }, { value: 'Cao', label: 'Cao' }, { value: 'KhanCap', label: 'Khẩn cấp' }], value: 'TrungBinh' },
      { name: 'moTa', label: 'Mô tả sự cố', type: 'textarea', required: true, placeholder: 'VD: hành lang bị hư đèn, cống thoát nước tắc...' }
    ],
    onSubmit: async v => {
      await window.bridge.call('SU_CO_TAO', { mucDo: v.mucDo, moTa: v.moTa });
      toast('Đã gửi báo cáo sự cố.', 'ok');
      await napSuCo();
    }
  });
}

document.addEventListener('DOMContentLoaded', () => {
  const hoTen = new URLSearchParams(window.location.search).get('u');
  if (hoTen) document.getElementById('pname').textContent = hoTen;
  document.getElementById('btnStaffReportIssue')?.addEventListener('click', openStaffIssueModal);
  document.querySelectorAll('.nav-btn[data-tab]').forEach(b =>
    b.addEventListener('click', () => loadNhanVienTab(b.dataset.tab)));
  loadNhanVienTab('tasks');
  startAutoRefresh(() => loadNhanVienTab(_activeTab), 30000);
});
