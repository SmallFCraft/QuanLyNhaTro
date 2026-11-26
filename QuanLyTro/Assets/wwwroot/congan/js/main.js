// Màn hình Công an phường (BR-16): khung 3 tab + điều hướng.
// Tiện ích dùng chung ở ../shared/js/ui.js.
const POLICE_TITLES = {
  citizens: 'Tra cứu công dân',
  residence: 'Báo cáo lưu trú & tạm trú',
  history: 'Lịch sử biến động lưu trú'
};

// Cache client-side dùng chung cho tab Công dân và Tạm trú.
let policeCache = null;
let policeRoomsCache = null;
let _currentPTab = 'citizens';

async function loadPoliceTab(tabKey) {
  _currentPTab = tabKey || _currentPTab;
  document.getElementById('ptitle').textContent = POLICE_TITLES[tabKey] || '';
  Object.keys(POLICE_TITLES).forEach(k => {
    const el = document.getElementById('ptab-' + k);
    if (el) el.hidden = (k !== tabKey);
  });
  document.querySelectorAll('#congan .tabs .tab').forEach(t => {
    t.classList.toggle('on', t.dataset.ptab === tabKey);
  });

  try {
    if (tabKey === 'citizens' || tabKey === 'residence') {
      await ensurePoliceCache();
      renderPoliceKpis();
      syncRoomDropdowns();
      renderCitizens(policeCache);
      renderResidence(policeCache);
    }
    if (tabKey === 'history') {
      await ensurePoliceCache();
      syncRoomDropdowns();
      await loadHistory();
    }
  } catch (err) {
    // Lỗi mạng / thiếu quyền: bảng phải nói rõ lý do, KHÔNG được để lại dòng mẫu tĩnh
    // khiến người dùng tưởng là dữ liệu thật.
    toast(err.message, 'err');
    renderPoliceError(err.message);
  }
}

/** Xóa mọi dòng mẫu và hiện đúng một dòng lý do cho cả 3 tab. */
function renderPoliceError(message) {
  const cols = { citizens: 6, residence: 7, history: 6 };
  for (const [tab, span] of Object.entries(cols)) {
    const body = document.querySelector(`#ptab-${tab} tbody`);
    if (body) {
      body.innerHTML = `<tr><td colspan="${span}" style="text-align:center;color:var(--dim)">Không tải được dữ liệu: ${esc(message)}</td></tr>`;
    }
  }
  renderPoliceKpis();
}

async function ensurePoliceCache() {
  if (policeCache && policeRoomsCache) return;
  // ponytail: KHACH_THUE_LAY_THEO_PHONG là hanh_dong đọc duy nhất cho Công an (BR-16) nên phải hỏi
  // từng phòng (N+1). Thêm TENANT_GET_ALL vào ma trận quyền khi số phòng lớn.
  const phong = (await window.bridge.call('PHONG_LAY_TAT_CA', {})) || [];
  const all = [];
  for (const r of phong) {
    const khach_thue = (await window.bridge.call('KHACH_THUE_LAY_THEO_PHONG', { phongId: r.id })) || [];
    // KhachThueDto không có soPhong -> gắn từ phòng đang lặp.
    for (const t of khach_thue) all.push({ ...t, soPhong: r.soPhong });
  }
  policeCache = all;
  policeRoomsCache = phong;
}

/**
 * 4 thẻ KPI + badge tab đều suy từ cache thật — không còn số tĩnh trong index.html.
 * "Người đang lưu trú" = khách có phòng (không tính khách tự đăng ký chờ gán phòng,
 * vì server chặn Công an không đọc được danh sách đó — BR-16).
 */
function renderPoliceKpis() {
  const all = policeCache || [];
  const phong = policeRoomsCache || [];
  const registered = all.filter(t => t.daDangKyTamTru).length;
  const unregistered = all.length - registered;
  const rate = all.length ? (registered * 100) / all.length : 0;

  const set = (id, text) => {
    const el = document.getElementById(id);
    if (el) el.textContent = text;
  };
  set('kpi-rooms', phong.length);
  set('kpi-tenants', all.length);
  set('kpi-tenants-sub', `Trên ${phong.length} phòng`);
  set('kpi-registered', registered);
  set('kpi-registered-sub', all.length ? `${rate.toLocaleString('vi-VN', { maximumFractionDigits: 1 })}%` : '—');
  set('kpi-unregistered', unregistered);
  set('cnt-residence', unregistered);
}

/** Dựng option phòng từ danh sách thật, giữ lựa chọn đang có. */
function syncRoomDropdowns() {
  const phong = policeRoomsCache || [];
  const opts = [
    '<option value="">Tất cả phòng</option>',
    ...phong.map(r => `<option value="${esc(r.soPhong)}">${esc(r.soPhong)} (${r.soNguoiHienTai ?? 0}/${r.soNguoiToiDa})</option>`)
  ].join('');
  for (const id of ['p-room-filter', 'p-res-room-filter', 'p-hist-room']) {
    const sel = document.getElementById(id);
    if (!sel) continue;
    const keep = sel.value;
    sel.innerHTML = opts;
    sel.value = keep;
  }
}

// ── Form tra cứu + bảng kết quả (PM-02/03) ─────────────────────────────────────
// Dùng XUAT_HO_SO_TAM_TRU cho cả tra cứu lẫn xuất: server lọc theo tiêu chí và trả JSON.
async function searchPolice() {
  const payload = {
    cccd: document.getElementById('polCccd')?.value?.trim() || '',
    hoTen: document.getElementById('polName')?.value?.trim() || '',
    soDienThoai: document.getElementById('polPhone')?.value?.trim() || '',
    soPhong: document.getElementById('polRoom')?.value?.trim() || ''
  };
  const count = document.getElementById('policeCountText');
  if (!Object.values(payload).some(v => v)) {
    renderPoliceResults([]);
    if (count) count.textContent = 'Nhập ít nhất một tiêu chí để tra cứu';
    return;
  }
  try {
    const rows = await window.bridge.call('XUAT_HO_SO_TAM_TRU', payload) || [];
    renderPoliceResults(rows);
    if (count) count.textContent = `Tìm thấy ${rows.length} bản ghi`;
  } catch (err) {
    renderPoliceResults([]);
    if (count) count.textContent = '';
    toast(err.message, 'err');
  }
}

function renderPoliceResults(rows) {
  const body = document.getElementById('policeTableBody');
  if (!body) return;
  body.innerHTML = (rows || []).map(t => `
    <tr>
      <td><b>${esc(t.hoTen)}</b></td>
      <td class="mono">${esc(t.cccd)}</td>
      <td class="mono">${esc(t.soDienThoai ?? '')}</td>
      <td>${esc(t.soPhong ?? '')}</td>
      <td>${esc(t.tenKhu ?? '')}</td>
      <td class="num"><span class="tag ${t.daDangKyTamTru ? 'done' : 'warn'}">${t.daDangKyTamTru ? 'Đã đăng ký' : 'Chưa đăng ký'}</span></td>
    </tr>`).join('') || '<tr><td colspan="6" style="text-align:center;color:var(--dim)">Không tìm thấy bản ghi nào</td></tr>';
}

async function exportPoliceResults() {
  try {
    const payload = {
      cccd: document.getElementById('polCccd')?.value?.trim() || '',
      hoTen: document.getElementById('polName')?.value?.trim() || '',
      soDienThoai: document.getElementById('polPhone')?.value?.trim() || '',
      soPhong: document.getElementById('polRoom')?.value?.trim() || ''
    };
    if (!Object.values(payload).some(v => v)) {
      toast('Nhập ít nhất một tiêu chí trước khi xuất.', 'info');
      return;
    }
    const csv = await window.bridge.call('XUAT_HO_SO_TAM_TRU', { ...payload, format: 'csv' });
    if (!csv) { toast('Không có bản ghi nào để xuất.', 'info'); return; }
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = `TraCuuCongDan_${new Date().toISOString().slice(0, 10)}.csv`;
    document.body.appendChild(a); a.click(); document.body.removeChild(a);
    URL.revokeObjectURL(a.href);
    toast('Đã xuất kết quả tra cứu ra file CSV.', 'ok');
  } catch (err) {
    toast(err.message, 'err');
  }
}

document.addEventListener('DOMContentLoaded', () => {
  // auth.js điều hướng sang đây kèm ?u=<tên đã encode>.
  const name = new URLSearchParams(window.location.search).get('u');
  if (name) document.getElementById('pname').textContent = name;
  loadPoliceTab('citizens');

  // Sidebar: mỗi mục mở đúng pane/tab thật của shell (trước đây data-police-view không có handler).
  document.querySelectorAll('.sidebar [data-police-tab]').forEach(btn => {
    btn.addEventListener('click', async () => {
      await loadPoliceTab(btn.dataset.policeTab);
      if (btn.dataset.policeFilter) {
        const filter = document.getElementById('p-res-status-filter');
        if (filter) { filter.value = btn.dataset.policeFilter; filter.dispatchEvent(new Event('change')); }
      }
      document.querySelectorAll('.sidebar .nav-btn').forEach(b => b.classList.toggle('active', b === btn));
      document.querySelector('.sidebar')?.classList.remove('open');
    });
  });
  document.querySelectorAll('.sidebar [data-police-focus]').forEach(btn => {
    btn.addEventListener('click', () => {
      document.getElementById(btn.dataset.policeFocus)?.focus();
      document.querySelectorAll('.sidebar .nav-btn').forEach(b => b.classList.toggle('active', b === btn));
      document.querySelector('.sidebar')?.classList.remove('open');
    });
  });
  document.getElementById('policeSidebarExport')?.addEventListener('click', () => {
    if (typeof exportCitizens === 'function') exportCitizens();
  });

  document.getElementById('policeSearchForm')?.addEventListener('submit', e => {
    e.preventDefault();
    searchPolice();
  });
  document.getElementById('btnPoliceExportExcel')?.addEventListener('click', exportPoliceResults);

  // ponytail: tự động làm mới toàn trang — thay nút sync tay. Reset cache buộc nạp lại từ server.
  startAutoRefresh(() => {
    policeCache = null;
    policeRoomsCache = null;
    return loadPoliceTab(_currentPTab);
  }, 30000);
});