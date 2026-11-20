// Shell Nhân viên — xem việc của mình, nhận việc, hoàn thành (ghi kết quả).
let viecCache = [];

const VIEC_TAG = {
  DaDuyet:   ['Mới', 'warn'],
  DangLam:   ['Đang làm', 'rent'],
  HoanThanh: ['Hoàn thành', 'done'],
  TuChoi:    ['Đã hủy', 'bad']
};

function loadNhanVienTab(key) {
  if (key !== 'viec') return;
  document.getElementById('ptitle').textContent = 'Việc được giao';
  napViec();
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
    const [label, tag] = VIEC_TAG[v.trangThai] || [v.trangThai, 'warn'];
    const tg = new Date(v.thoiGian).toLocaleString('vi-VN');
    const hanhDong = v.trangThai === 'DaDuyet'
      ? `<button class="btn btn-primary btn-sm" onclick="nhanViec(${v.id})"><i class="fas fa-hand-pointer"></i> Nhận việc</button>`
      : v.trangThai === 'DangLam'
        ? `<button class="btn btn-primary btn-sm" onclick="hoanThanhViec(${v.id})"><i class="fas fa-check-double"></i> Hoàn thành</button>`
        : `<span class="tag ${tag}">${v.ketQua ? esc(v.ketQua) : '—'}</span>`;
    return `<tr>
      <td><b>${esc(v.soPhong)}</b></td>
      <td>${esc(v.hoTen)}<br><span class="mono" style="font-size:11px">${esc(v.soDienThoai)}</span></td>
      <td class="mono">${esc(tg)}</td>
      <td><span class="tag ${tag}">${esc(label)}</span></td>
      <td>${v.lyDoTuChoi ? esc(v.lyDoTuChoi) : '—'}</td>
      <td style="white-space:nowrap">${hanhDong}</td>
    </tr>`;
  }).join('');

  el.innerHTML = `
    <div class="tblwrap">
    <table class="tbl">
      <thead><tr><th>Phòng</th><th>Khách</th><th>Thời gian</th><th>Trạng thái</th><th>Ghi chú</th><th></th></tr></thead>
      <tbody>${rows || '<tr><td colspan="6" style="text-align:center;color:var(--dim)">Chưa có việc nào</td></tr>'}</tbody>
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

document.addEventListener('DOMContentLoaded', () => {
  const hoTen = new URLSearchParams(window.location.search).get('u');
  if (hoTen) document.getElementById('pname').textContent = hoTen;
  loadNhanVienTab('viec');
});
