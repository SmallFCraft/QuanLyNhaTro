// Tab Biến động (PM-05): lịch sử theo khoảng ngày #p-from → #p-to + xuất file.
function historyRange() {
  const pad = n => String(n).padStart(2, '0');
  const today = new Date();
  // Mặc định = tháng hiện tại, không hard-code "2026-09" trong HTML.
  const firstOfMonth = `${today.getFullYear()}-${pad(today.getMonth() + 1)}-01`;
  const todayIso = `${today.getFullYear()}-${pad(today.getMonth() + 1)}-${pad(today.getDate())}`;
  const fromEl = document.getElementById('p-from');
  const toEl = document.getElementById('p-to');
  if (fromEl && !fromEl.value) fromEl.value = firstOfMonth;
  if (toEl && !toEl.value) toEl.value = todayIso;
  return {
    from: fromEl?.value || '',
    to: toEl?.value || ''
  };
}

async function loadHistory() {
  const { from, to } = historyRange();
  const soPhong = document.getElementById('p-hist-room')?.value || null;
  const loaiBienDong = document.getElementById('p-hist-type')?.value || null;
  const rows = await window.bridge.call('LICH_SU_CU_TRU_LAY', { from, to, soPhong, loaiBienDong });
  const body = document.querySelector('#ptab-history tbody');
  if (!body) return;
  const filtered = loaiBienDong
    ? (rows || []).filter(h => (h.loaiBienDong ?? h.LoaiBienDong ?? h.eventType) === loaiBienDong)
    : (rows || []);
  body.innerHTML = filtered.map(h => {
    const loai = h.loaiBienDong ?? h.LoaiBienDong ?? h.eventType ?? 'Đang ở';
    const ngay = h.ngayBienDong ?? h.eventDate;
    const ghiChu = h.ghiChu ?? h.ghi_chu ?? '—';
    const tagClass = loai === 'Trả phòng' ? 'warn' : (loai === 'Chuyển phòng' ? 'avail' : 'rent');
    return `
      <tr>
        <td><b>${esc(h.hoTen)}</b></td>
        <td><b>${esc(h.soPhong)}</b></td>
        <td><span class="tag ${tagClass}">${esc(loai)}</span></td>
        <td class="mono">${fmtDateOnly(ngay)}</td>
        <td class="mono">—</td>
        <td>${esc(ghiChu)}</td>
      </tr>`;
  }).join('') || `<tr class="empty"><td colspan="6">Không có biến động nào</td></tr>`;

  const count = filtered.length;
  const foot = document.querySelector('#ptab-history .tblfoot span');
  if (foot) foot.textContent = `Tổng: ${count} bản ghi`;
}

async function exportHistory() {
  const { from, to } = historyRange();
  try {
    const rows = await window.bridge.call('LICH_SU_CU_TRU_LAY', { from, to, soPhong: null, loaiBienDong: null }) || [];
    const cell = v => '"' + String(v ?? '').replace(/"/g, '""') + '"';
    const lines = [['Ho ten', 'So phong', 'Loai bien dong', 'Ngay', 'Ghi chu'].map(cell).join(',')]
      .concat(rows.map(h => [h.hoTen, h.soPhong, h.loaiBienDong ?? h.eventType, h.ngayBienDong ?? h.eventDate, h.ghiChu].map(cell).join(',')));
    const blob = new Blob(['﻿' + lines.join('\r\n')], { type: 'text/csv;charset=utf-8' });
    const link = Object.assign(document.createElement('a'), { href: URL.createObjectURL(blob), download: `lich-su-cu-tru_${from}_${to}.csv` });
    link.click();
    URL.revokeObjectURL(link.href);
    toast(`Đã xuất ${rows.length} bản ghi.`, 'ok');
  } catch (err) {
    alertDialog(err.message, 'Xuất thất bại');
  }
}
