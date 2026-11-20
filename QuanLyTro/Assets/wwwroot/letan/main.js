// Shell Lễ tân — duyệt / từ chối / gán nhân viên cho lịch kiểm tra phòng.
let lichCache = [];
let nhanVienCache = [];

const LICH_TAG = {
  ChoDuyet:  ['Chờ duyệt', 'warn'],
  DaDuyet:   ['Đã duyệt', 'rent'],
  TuChoi:    ['Đã từ chối', 'bad'],
  DangLam:   ['Đang làm', 'rent'],
  HoanThanh: ['Hoàn thành', 'done']
};

function loadLeTanTab(key) {
  if (key !== 'lich') return;
  document.getElementById('ptitle').textContent = 'Lịch kiểm tra phòng';
  napLich();
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
    const [label, tag] = LICH_TAG[l.trangThai] || [l.trangThai, 'warn'];
    const tg = new Date(l.thoiGian).toLocaleString('vi-VN');
    const hanhDong = l.trangThai === 'ChoDuyet'
      ? `<button class="btn btn-primary btn-sm" onclick="duyetLich(${l.id})"><i class="fas fa-check"></i> Duyệt</button>
         <button class="btn btn-danger btn-sm" onclick="tuChoiLich(${l.id})"><i class="fas fa-ban"></i> Từ chối</button>`
      : l.trangThai === 'DaDuyet'
        ? `<select class="inp" id="nv-${l.id}" style="width:170px">
             <option value="">— Chọn nhân viên —</option>
             ${nhanVienCache.map(n => `<option value="${n.id}">${esc(n.hoTen)} (${esc(n.chuyenMon)})</option>`).join('')}
           </select>
           <button class="btn btn-primary btn-sm" onclick="ganNv(${l.id})"><i class="fas fa-user-plus"></i> Gán</button>`
        : `<span class="tag ${tag}">${l.nhanVienTen ? esc(l.nhanVienTen) : '—'}</span>`;
    return `<tr>
      <td><b>${esc(l.soPhong)}</b></td>
      <td>${esc(l.hoTen)}<br><span class="mono" style="font-size:11px">${esc(l.soDienThoai)}</span></td>
      <td class="mono">${esc(tg)}</td>
      <td><span class="tag ${tag}">${esc(label)}</span></td>
      <td>${l.lyDoTuChoi ? esc(l.lyDoTuChoi) : (l.ketQua ? esc(l.ketQua) : '—')}</td>
      <td style="white-space:nowrap">${hanhDong}</td>
    </tr>`;
  }).join('');

  el.innerHTML = `
    <div class="tblwrap">
    <table class="tbl">
      <thead><tr><th>Phòng</th><th>Khách</th><th>Thời gian</th><th>Trạng thái</th><th>Ghi chú</th><th></th></tr></thead>
      <tbody>${rows || '<tr><td colspan="6" style="text-align:center;color:var(--dim)">Chưa có lịch nào</td></tr>'}</tbody>
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

document.addEventListener('DOMContentLoaded', () => {
  const hoTen = new URLSearchParams(window.location.search).get('u');
  if (hoTen) document.getElementById('pname').textContent = hoTen;
  loadLeTanTab('lich');
});
