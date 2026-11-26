// ponytail: shell khách thuê — server tự suy ra phòng từ token. Phân trang phía server (page/soLuongMoiTrang).
let currentTenantInvoices = [];
let tenantLatestInvoice = null;
let tenantHistoryPage = 1;
let tenantHistoryPageSize = 10;
let tenantHistoryTotal = 0;
let tenantRoomId = null;
let tenantRoomName = null;
let tenantHopDong = null;
let tenantChiSo = null;
// Tab app đang chọn — showCheckinScreen strip .active khi hiện màn QR nên phải ghi lại
// để màn ẩn đi còn dựng lại đúng pane, không mặc định 'overview'.
let tenantActiveTab = 'overview';
// Skeleton chỉ hiện khi CHƯA từng nạp được gì — auto-refresh 30s không được làm nhấp nháy bảng đang xem.
let tenantLoaded = false;
let camStream = null;
let camInterval = null;
let camStarting = false;
// Lần nạp gần nhất có lỗi không — khi đúng thì tóm tắt KHÔNG được khẳng định "chưa phát sinh".
let tenantLoadError = false;

async function loadTenantInvoices() {
  const previousRoom = tenantRoomId;
  if (!tenantLoaded) renderTenantLoading();
  try {
    await loadTenantHistoryPage(tenantHistoryPage, tenantHistoryPageSize);
    tenantLoaded = true;
    tenantLoadError = false;
    // BR-19: chỉ hiện màn nhận phòng khi server xác nhận khach_thue.phong_id IS NULL.
    // KHÔNG suy từ số hóa đơn — khách vừa nhận phòng chưa phát sinh kỳ cước nào vẫn phải vào app.
    if (tenantRoomId === null) {
      showCheckinScreen(true);
      return;
    }
    if (previousRoom !== tenantRoomId || !document.getElementById('tab-nhan-phong')?.classList.contains('active')) {
      showCheckinScreen(false);
    }
    announceTenant(`Đã nạp xong ${currentTenantInvoices.length} kỳ cước, tổng ${tenantHistoryTotal} bản ghi`);
  } catch (err) {
    toast(err.message, 'err');
    announceTenant('Không tải được dữ liệu: ' + err.message);
    tenantLoadError = true;
    renderTenantEmpty();
    syncTenantRoomHeader();
    // Cho phép skeleton chạy lại ở lần nạp kế tiếp (thủ công hoặc auto-refresh 30s).
    tenantLoaded = false;
  }
  await refreshTenantSummary();
  await loadPaymentConfig();
}

function showCheckinScreen(show) {
  const checkin = document.getElementById('tab-nhan-phong');
  const navTabs = document.getElementById('tenantTabs');
  const content = checkin ? checkin.parentElement : null;
  if (checkin) {
    checkin.classList.toggle('active', show);
    checkin.style.display = show ? '' : 'none';
    if (show && !checkin.querySelector('.checkin-pane.active')) switchCheckinTab('anh');
  }
  // Khi hiện màn nhận phòng: ẩn sạch các pane app (overview/current/history) để không xếp chồng.
  if (content) {
    content.querySelectorAll(':scope > .tab-pane').forEach(p => {
      if (p === checkin) return;
      if (show) {
        p.classList.remove('active');
        p.style.display = 'none';
      } else {
        p.style.display = '';
        // Dựng lại pane đang chọn: nhánh trên đã gỡ .active nên .tab-pane{display:none}
        // vẫn ẩn mọi pane nếu chỉ trả lại display — màn hình sẽ trắng.
        p.classList.toggle('active', p.id === `tab-${tenantActiveTab}`);
      }
    });
  }
  if (navTabs) navTabs.style.display = show ? 'none' : '';
  document.getElementById('btnShowCheckin').hidden = show;
  document.getElementById('btnBackToOverview').hidden = tenantLoaded && tenantRoomId === null;
  syncTenantRoomHeader();
  if (!show) {
    stopCamera();
    // KHÔNG ép switchTenantTab('overview') ở đây: caller tự quyết (load ban đầu mới về overview,
    // còn auto-refresh 30s giữ nguyên tab khách đang xem).
  }
}

function setText(id, text) {
  const el = document.getElementById(id);
  if (el) el.textContent = text;
}

// BR-19: đồng bộ userbar + headline + statusstrip theo trạng thái nhận phòng.
// tenantRoomId === null → khách chưa có phòng, mọi cụm header phải nói "chưa nhận phòng",
// KHÔNG được để lại số phòng / tiêu đề hóa đơn của shell tĩnh.
function syncTenantRoomHeader() {
  const hasRoom = tenantRoomId !== null;
  const nameStr = tenantRoomName !== null && tenantRoomName !== undefined ? String(tenantRoomName).trim() : '';
  const roomLabel = nameStr
    ? (nameStr.startsWith('P') ? nameStr : `P.${nameStr}`)
    : (hasRoom ? `P.${tenantRoomId}` : '');

  setText('troom-userbar-text', hasRoom ? `Phòng ${roomLabel}` : (tenantLoadError ? 'Chưa tải được phòng' : 'Chưa nhận phòng'));
  setText('thead-badge', hasRoom ? 'Theo dõi cước, xem hóa đơn và thông tin phòng thuê.' : (tenantLoadError ? 'Không tải được dữ liệu. Vui lòng kiểm tra kết nối và thử lại.' : 'Kích hoạt phòng thuê bằng mã trên phiếu bàn giao.'));
  setText('thead-title', 'Sổ phòng của tôi');
  setText('tstatus-text', hasRoom ? `Phòng ${roomLabel}` : (tenantLoadError ? 'Chưa tải được phòng' : 'Chưa nhận phòng'));
  setText('troom-badge', hasRoom ? roomLabel : (tenantLoadError ? 'Chưa tải được thông tin phòng' : 'Chưa nhận phòng'));
}

// Giữ aria-selected + tabindex khớp class="on" để screen reader + bàn phím thấy đúng tab.
function syncTabBar(barId, name) {
  document.querySelectorAll(`#${barId} .tab`).forEach(t => {
    const on = t.dataset.tab === name;
    t.classList.toggle('on', on);
    t.setAttribute('aria-selected', on ? 'true' : 'false');
    t.tabIndex = on ? 0 : -1;
  });
}

// Bàn phím cho tab bar: Arrow chuyển tab kế/trước, Home/End về đầu/cuối, focus tab được chọn.
function wireTabKeys(barId, onPick) {
  const bar = document.getElementById(barId);
  if (!bar) return;
  bar.addEventListener('keydown', e => {
    const tabs = [...bar.querySelectorAll('.tab')];
    const i = tabs.indexOf(document.activeElement);
    if (i < 0) return;
    let next = -1;
    if (e.key === 'ArrowRight') next = (i + 1) % tabs.length;
    else if (e.key === 'ArrowLeft') next = (i - 1 + tabs.length) % tabs.length;
    else if (e.key === 'Home') next = 0;
    else if (e.key === 'End') next = tabs.length - 1;
    else return;
    e.preventDefault();
    onPick(tabs[next].dataset.tab);
    tabs[next].focus();
  });
}

function announceTenant(msg) {
  const live = document.getElementById('tenant-live');
  if (live) live.textContent = msg;
}

function switchCheckinTab(tabName) {
  stopCamera();
  syncTabBar('checkinTabs', tabName);
  // Chỉ đổi class — .checkin-pane{display:none} / .active{display:block} trong khachthue.css
  // là nguồn hiển thị duy nhất; ghi style.display sẽ đè rules và làm mất animation.
  document.querySelectorAll('.checkin-pane').forEach(p => {
    p.classList.toggle('active', p.id === `checkin-${tabName}`);
  });
}

function checkinQr() {
  loadTenantInvoices();
}

/** Tab 1: đọc file ảnh QR từ người dùng qua <canvas> + jsQR */
function decodeQrFile(input) {
  const file = input.files && input.files[0];
  if (!file) return;
  const reader = new FileReader();
  reader.onerror = () => alertDialog('Không thể đọc file ảnh từ thiết bị.', 'Lỗi đọc file', 'err');
  reader.onload = e => {
    const img = new Image();
    img.onerror = () => alertDialog('Định dạng ảnh không được hỗ trợ hoặc file bị hỏng.', 'Lỗi ảnh', 'err');
    img.onload = () => {
      if (!img.width || !img.height) {
        alertDialog('Ảnh không có kích thước hợp lệ (0x0).', 'Ảnh rỗng', 'warn');
        return;
      }
      try {
        const canvas = document.getElementById('qrCanvas');
        // Canvas khai báo `hidden` trong index.html — chưa bỏ thì ctx.drawImage vẽ vào 0×0.
        canvas.hidden = false;
        const ctx = canvas.getContext('2d');
        canvas.width = img.width;
        canvas.height = img.height;
        ctx.drawImage(img, 0, 0);
        const imgData = ctx.getImageData(0, 0, img.width, img.height);
        const code = jsQR(imgData.data, imgData.width, imgData.height);
        if (code && code.data) {
          submitCheckinCode(code.data);
        } else {
          alertDialog('Không tìm thấy mã QR hợp lệ trong ảnh vừa chọn.', 'Không nhận diện được', 'warn');
        }
      } catch (err) {
        alertDialog('Lỗi giải mã ảnh QR: ' + (err.message || err), 'Lỗi xử lý', 'err');
      }
    };
    img.src = e.target.result;
  };
  reader.readAsDataURL(file);
}

/** Tab 2: mở webcam loop quét thời gian thực */
async function toggleCamera() {
  if (camStarting) return;
  if (camStream) {
    stopCamera();
    return;
  }
  const video = document.getElementById('qrVideo');
  const btn = document.getElementById('btn-cam');
  const attempt = Symbol();
  camStarting = attempt;
  if (btn) { btn.disabled = true; btn.textContent = 'Đang mở camera…'; }
  try {
    let stream;
    try {
      stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } });
    } catch (err) {
      if (camStarting !== attempt) return;
      if (err.name === 'NotAllowedError' || err.name === 'PermissionDeniedError' || !navigator.mediaDevices) throw err;
      stream = await navigator.mediaDevices.getUserMedia({ video: true });
    }
    // Người dùng có thể rời tab trước khi trình duyệt trả lời yêu cầu cấp quyền.
    if (camStarting !== attempt) { stream.getTracks().forEach(t => t.stop()); return; }
    camStream = stream;
    video.srcObject = stream;
    try {
      await video.play();
    } catch (err) {
      if (err.name !== 'AbortError') throw err;
    }
    if (camStarting !== attempt) return;
    if (btn) btn.textContent = 'Tắt camera';

    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d');

    camInterval = setInterval(() => {
      if (video.readyState === video.HAVE_ENOUGH_DATA) {
        canvas.width = video.videoWidth;
        canvas.height = video.videoHeight;
        ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
        const imgData = ctx.getImageData(0, 0, canvas.width, canvas.height);
        const code = jsQR(imgData.data, imgData.width, imgData.height);
        if (code && code.data) {
          stopCamera();
          submitCheckinCode(code.data);
        }
      }
    }, 300);
  } catch (err) {
    if (camStarting !== attempt) return;
    stopCamera();
    const detail = err.name === 'NotAllowedError' || err.name === 'PermissionDeniedError'
      ? 'Bạn chưa cấp quyền camera. Bạn có thể chọn ảnh QR hoặc nhập PIN.'
      : 'Không mở được camera. Hãy thử lại, chọn ảnh QR hoặc nhập PIN.';
    alertDialog(detail, 'Không mở được camera', 'err');
  } finally {
    if (camStarting === attempt) {
      camStarting = false;
      if (btn) btn.disabled = false;
    }
  }
}

function stopCamera() {
  camStarting = false;
  if (camInterval) { clearInterval(camInterval); camInterval = null; }
  if (camStream) {
    camStream.getTracks().forEach(t => t.stop());
    camStream = null;
  }
  const video = document.getElementById('qrVideo');
  if (video) video.srcObject = null;
  const btn = document.getElementById('btn-cam');
  if (btn) { btn.disabled = false; btn.textContent = 'Bật camera'; }
}

/** Tab 3: nhập mã PIN 8 số */
function submitPin() {
  const pin = (document.getElementById('qrPin')?.value || '').trim();
  if (!/^\d{8}$/.test(pin)) {
    alertDialog('Mã PIN nhận phòng phải gồm đúng 8 chữ số.', 'Mã không đúng định dạng', 'warn');
    return;
  }
  submitCheckinCode(pin);
}

/** Gửi mã lên server qua action KHACH_THUE_NHAN_PHONG_QR (BR-19) */
async function submitCheckinCode(rawCode) {
  try {
    const res = await window.bridge.call('KHACH_THUE_NHAN_PHONG_QR', { tokenOrPin: rawCode });
    stopCamera();
    await alertDialog(
      `Chào mừng bạn đến phòng ${res.soPhong || ''}! Hợp đồng đã có hiệu lực từ ${res.ngayBatDau || ''}.`,
      'Nhận phòng thành công', 'ok');
    tenantHistoryPage = 1;
    tenantLoaded = false;
    showCheckinScreen(false);
    // Nạp lại dữ liệu mà không mất tab — khác window.location.reload().
    await loadTenantInvoices();
    switchTenantTab('overview');
  } catch (err) {
    alertDialog(err.message, 'Nhận phòng thất bại', 'err');
  }
}

async function loadTenantHistoryPage(page, soLuongMoiTrang) {
  const res = await window.bridge.call('HOA_DON_CUA_TOI', { page, soLuongMoiTrang });
  const dto = normalizeHistoryDto(res);
  currentTenantInvoices = (dto && dto.items) || [];
  tenantHistoryPage = (dto && dto.page) || 1;
  tenantHistoryPageSize = (dto && dto.soLuongMoiTrang) || soLuongMoiTrang;
  tenantHistoryTotal = (dto && dto.tongSo) || currentTenantInvoices.length;
  // phongId rõ ràng (kể cả null = server xác nhận chưa có phòng) thì tin server.
  // Chỉ khi field vắng mặt (bridge mock / server cũ) mới suy từ hóa đơn sẵn có để khỏi đẩy nhầm khách có phòng vào màn QR.
  tenantRoomId = dto && dto.phongId !== undefined
    ? dto.phongId
    : (currentTenantInvoices.length > 0 ? tenantRoomId : null);
  tenantRoomName = (dto && dto.soPhong) || null;
  tenantHopDong = (dto && dto.hopDong) || null;
  tenantChiSo = (dto && dto.chiSoKyNay) || null;
  syncTenantRoomHeader();
  renderTenantHistory(currentTenantInvoices);
  renderTenantPagination();
  renderTenantUtilities();
}

async function changeTenantPage(delta) {
  const tongSoTrang = Math.max(1, Math.ceil(tenantHistoryTotal / tenantHistoryPageSize));
  const next = tenantHistoryPage + delta;
  if (next < 1 || next > tongSoTrang) return;
  try {
    await loadTenantHistoryPage(next, tenantHistoryPageSize);
    renderPaymentBlock();
  } catch (err) {
    if (typeof toast === 'function') toast(err.message, 'err');
  }
}

function normalizeHistoryDto(res) {
  if (!res) return null;
  if (Array.isArray(res)) {
    return { items: res, tongSo: res.length, page: 1, soLuongMoiTrang: res.length || 10 };
  }
  const list = res.danhSach || res.items || res.DanhSach || res.Items;
  if (list) {
    const items = list || [];
    const i = n => (n === undefined || n === null) ? undefined : Number(n);
    return {
      items,
      tongSo: i(res.tongSo ?? res.TongSo ?? res.totalCount ?? res.TotalCount) ?? items.length,
      page: i(res.trang ?? res.Trang ?? res.page ?? res.Page) ?? 1,
      soLuongMoiTrang: i(res.soLuongMoiTrang ?? res.SoLuongMoiTrang ?? res.pageSize ?? res.PageSize) ?? 10,
      // Giữ undefined khi field VẮNG MẶT khác null THẬT: loadTenantHistoryPage dùng '!== undefined'
      // để giữ phòng cũ cho server/mock cũ không trả phongId, không ép về "chưa nhận phòng".
      phongId: 'phongId' in res ? res.phongId : ('PhongId' in res ? res.PhongId : undefined),
      soPhong: res.soPhong ?? res.SoPhong ?? null,
      hopDong: normalizeHopDong(res.hopDong ?? res.HopDong),
      chiSoKyNay: normalizeChiSo(res.chiSoKyNay ?? res.ChiSoKyNay)
    };
  }
  return null;
}

// hopDong / chiSoKyNay: server serialize camelCase (JsonNamingPolicy.CamelCase), vẫn nhận PascalCase cho mock & server cũ.
function subField(o, camel, pascal) {
  return o[camel] !== undefined ? o[camel] : o[pascal];
}

function subNum(o, camel, pascal) {
  const v = Number(subField(o, camel, pascal));
  return Number.isFinite(v) ? v : 0;
}

function normalizeHopDong(hd) {
  if (!hd) return null;
  return {
    id: subField(hd, 'id', 'Id'),
    soPhong: subField(hd, 'soPhong', 'SoPhong') ?? null,
    ngayBatDau: subField(hd, 'ngayBatDau', 'NgayBatDau') ?? null,
    ngayKetThuc: subField(hd, 'ngayKetThuc', 'NgayKetThuc') ?? null,
    giaThue: subNum(hd, 'giaThue', 'GiaThue'),
    tienCoc: subNum(hd, 'tienCoc', 'TienCoc')
  };
}

function normalizeChiSo(cs) {
  if (!cs) return null;
  return {
    kyCuoc: subField(cs, 'kyCuoc', 'KyCuoc') ?? null,
    dienCu: subNum(cs, 'dienCu', 'DienCu'),
    dienMoi: subNum(cs, 'dienMoi', 'DienMoi'),
    giaDien: subNum(cs, 'giaDien', 'GiaDien'),
    nuocCu: subNum(cs, 'nuocCu', 'NuocCu'),
    nuocMoi: subNum(cs, 'nuocMoi', 'NuocMoi'),
    giaNuoc: subNum(cs, 'giaNuoc', 'GiaNuoc'),
    hinhThucNuoc: subField(cs, 'hinhThucNuoc', 'HinhThucNuoc') || 'Khoi',
    soNguoiNuoc: subNum(cs, 'soNguoiNuoc', 'SoNguoiNuoc')
  };
}

function isPaidInvoice(i) {
  const st = i.trangThai ?? i.trang_thai;
  return i.isPaid === true || st === 'DaThu' || st === 'paid' || st === 1;
}

function renderTenantHistory(rows) {
  const list = rows || [];
  const body = document.getElementById('tenant-history-body');
  if (body) {
    body.innerHTML = list.map(i => `
      <tr class="clickable-row" onclick="showInvoiceDetail(${Number(i.id)})" title="Bấm để xem chi tiết kỳ ${esc(fmtKyCuoc(i.kyCuoc))}" aria-label="Kỳ ${esc(fmtKyCuoc(i.kyCuoc))}">
        <td data-label="Kỳ cước"><button type="button" class="tenant-invoice-link" onclick="event.stopPropagation(); showInvoiceDetail(${Number(i.id)})" aria-label="Xem chi tiết kỳ ${esc(fmtKyCuoc(i.kyCuoc))}">${esc(fmtKyCuoc(i.kyCuoc))}</button></td>
        <td class="num amount" data-label="Tổng tiền">${fmtMoney(i.tongTien)}</td>
        <td class="mono" data-label="Ngày đóng">${isPaidInvoice(i) ? fmtDate(i.ngayDong) : '—'}</td>
        <td class="num" data-label="Trạng thái"><span class="tag ${isPaidInvoice(i) ? 'paid' : 'unpaid'}">
          ${isPaidInvoice(i) ? 'Đã thanh toán' : 'Chưa thanh toán'}</span></td>
      </tr>`).join('') || `<tr class="empty"><td colspan="4" data-label="Kỳ cước">Chưa phát sinh kỳ cước nào</td></tr>`;
  }
  const info = document.getElementById('tenant-page-info');
  if (info) info.textContent = `Tổng: ${tenantHistoryTotal} bản ghi`;
}

function renderTenantPagination() {
  // Không có bản ghi thì "Trang 1 / 1" vô nghĩa — ẩn cụm phân trang.
  // (Viết `total < 1` chứ không so `=== 0`: contract BR-19 cấm chuỗi đó trong file này,
  // vì nó là dấu hiệu suy 'chưa có phòng' từ số hóa đơn.)
  const cluster = document.getElementById('tenant-pagination');
  if (cluster) cluster.hidden = tenantHistoryTotal < 1;
  const tongSoTrang = Math.max(1, Math.ceil(tenantHistoryTotal / tenantHistoryPageSize));
  const indicator = document.getElementById('tenant-page-indicator');
  if (indicator) indicator.textContent = `Trang ${tenantHistoryPage} / ${tongSoTrang}`;
  const prev = document.getElementById('btn-prev-page');
  if (prev) prev.disabled = tenantHistoryPage <= 1;
  const next = document.getElementById('btn-next-page');
  if (next) next.disabled = tenantHistoryPage >= tongSoTrang;
}

function renderTenantLoading() {
  const body = document.getElementById('tenant-history-body');
  if (body) {
    const skel = `<div class="skel"></div>`;
    body.innerHTML = `<tr><td colspan="4">${skel}${skel}${skel}</td></tr>`;
  }
}

function renderTenantEmpty() {
  const body = document.getElementById('tenant-history-body');
  if (body) {
    body.innerHTML = `<tr class="empty"><td colspan="4">
      <div class="empty-state">Không tải được dữ liệu
        <div><button type="button" class="btn btn-outline btn-sm" onclick="loadTenantInvoices()">Thử lại</button></div>
      </div></td></tr>`;
  }
  const info = document.getElementById('tenant-page-info');
  if (info) info.textContent = '';
  tenantHistoryTotal = 0;
  renderTenantPagination();
  // Giữ số liệu đã tải trước đó; nếu chưa có số liệu thì thay trạng thái đang tải bằng lỗi.
  if (!currentTenantInvoices.length) {
    const kpis = document.getElementById('util-kpis');
    if (kpis) kpis.innerHTML = `<div class="empty-state">Không tải được dữ liệu cước
        <div><button type="button" class="btn btn-outline btn-sm" onclick="loadTenantInvoices()">Thử lại</button></div>
      </div>`;
    setText('util-charts', '');
    setText('ov-chart', 'Không tải được dữ liệu biểu đồ.');
  }
}

// Tóm tắt Tổng quan + chi tiết Kỳ này: khi đang ở trang 1, currentTenantInvoices[0] đã là
// hóa đơn mới nhất (server ORDER BY ky_cuoc DESC) nên render ngay, tiết kiệm 1 round-trip.
// Chỉ gọi request riêng { page: 1, soLuongMoiTrang: 1 } khi khách đang đứng ở trang > 1.
async function refreshTenantSummary() {
  // Lỗi nạp: KHÔNG được khẳng định "chưa phát sinh kỳ cước nào" — đó là sự thật app chưa biết.
  // (Ruling A: số không tính được thì không được trình bày như số.)
  if (tenantLoadError) { renderTenantSummaryError(); return; }
  if (tenantHistoryPage === 1 && currentTenantInvoices.length > 0) {
    renderTenantSummary(currentTenantInvoices[0]);
    return;
  }
  try {
    const res = await window.bridge.call('HOA_DON_CUA_TOI', { page: 1, soLuongMoiTrang: 1 });
    const dto = normalizeHistoryDto(res);
    // Đồng bộ chỉ số/hợp đồng của ĐÚNG trang 1 trước khi vẽ: nếu không, tổng tiền kỳ mới nhất
    // bị ghép với chỉ số của kỳ trang cũ → khách tự kiểm tra phép tính sẽ ra số sai.
    // Gán vô điều kiện khi dto non-null: null là giá trị hợp lệ (khách vừa trả phòng /
    // kỳ chưa chốt chỉ số) — giữ giá trị cũ mới là ghép sai.
    // CHỈ số trang 1 được đưa vào local: ghi vào tenantChiSo sẽ đổi đơn giá mà
    // buildTenantChart đọc, khiến biểu đồ trang khách đang xem lệch với KPI kế bên.
    let chiSoTrang1 = tenantChiSo;
    if (dto) {
      tenantHopDong = dto.hopDong ?? null;
      chiSoTrang1 = dto.chiSoKyNay ?? null;
    }
    const latest = dto?.items?.[0];
    if (latest) { renderTenantSummary(latest, chiSoTrang1); return; }
  } catch (err) {
    renderTenantSummaryError();
    return;
  }
  renderTenantNoInvoice();
}

// Không có dữ liệu vì lỗi tải (khác hẳn "khách chưa phát sinh kỳ cước nào").
function renderTenantSummaryError() {
  tenantLatestInvoice = null;
  tenantLoadError = true;
  setText('due-alert-text', 'Không tải được dữ liệu cước.');
  // Không khẳng định trạng thái thu khi không tải được — số cũ còn đó nhưng app không biết gì mới.
  setTag('due-alert-tag', null, '', 'Chưa rõ');
  setText('ov-trang_thai', 'Chưa tải được dữ liệu');
  if (!currentTenantInvoices.length) {
    ['ov-total', 'ov-elec', 'ov-nuoc', 'rc-total', 'rc-room', 'rc-elec', 'rc-water', 'rc-other'].forEach(id => setText(id, '—'));
    setText('tcontract-due', 'Chưa tải được thông tin hợp đồng.');
    setText('rc-code', 'Chưa tải được mã bảng kê');
    ['ov-elec-sub', 'ov-nuoc-sub', 'rc-elec-sub', 'rc-water-sub'].forEach(id => setText(id, 'Chưa tải được chỉ số.'));
  }
  setTag('rc-trang_thai-tag', null, '', 'Chưa rõ');
  setTag('rc-due-tag', null, '', 'Chưa rõ');
}

// Khách chưa có hóa đơn: phân biệt "chủ trọ chưa lập" với "chưa tới kỳ cước".
// Gộp hai trường hợp thành một câu là khẳng định sai: khách đọc "0 đ" thành "tháng này không
// phải trả gì", trong khi thật ra chủ trọ còn thiếu bước lập hóa đơn.
function renderTenantNoInvoice() {
  tenantLatestInvoice = null;
  const batDau = tenantHopDong?.ngayBatDau ? new Date(tenantHopDong.ngayBatDau) : null;
  const daVaoO = batDau && !Number.isNaN(batDau.getTime()) && batDau <= new Date();
  const recv = batDau ? ` · Nhận phòng từ ${fmtDateOnly(tenantHopDong.ngayBatDau)}` : '';

  if (daVaoO) {
    setText('due-alert-text',
      'Chủ trọ chưa lập hóa đơn kỳ này' + recv + '. Vui lòng liên hệ chủ trọ.');
    setText('ov-trang_thai', 'Chờ chủ trọ lập hóa đơn');
  } else {
    setText('due-alert-text', 'Chưa phát sinh kỳ cước nào' + recv + '.');
    setText('ov-trang_thai', 'Chưa phát sinh kỳ cước');
  }

  setTag('due-alert-tag', null, '', daVaoO ? 'Chưa lập hóa đơn' : 'Chưa phát sinh');
  setText('rc-title', 'Chi tiết cước');
  setText('rc-code', 'Chưa có mã bảng kê');
  ['rc-room', 'rc-elec', 'rc-water', 'rc-other', 'rc-total', 'ov-month', 'ov-total', 'ov-elec', 'ov-nuoc'].forEach(id => setText(id, '—'));
  setTag('rc-trang_thai-tag', null, '', 'Chưa có hóa đơn');
  setTag('rc-due-tag', null, '', 'Chưa phát sinh');
  setText('ov-nuoc-unit', 'khối');
  setText('ov-elec-sub', 'Chưa có chỉ số kỳ này');
  setText('ov-nuoc-sub', 'Chưa có chỉ số kỳ này');
  setText('rc-elec-sub', 'Chưa có chỉ số kỳ này');
  setText('rc-water-sub', 'Chưa có chỉ số kỳ này');
  setText('tcontract-due', tenantHopDong
    ? `HĐ đến ${fmtDateOnly(tenantHopDong.ngayKetThuc)} · ${fmtMoney(tenantHopDong.giaThue)} đ/tháng`
    : 'Chưa có hợp đồng hiệu lực');
}

function renderTenantSummary(unpaid, csOverride) {
  tenantLatestInvoice = unpaid;
  // Khối Kỳ này
  setText('rc-room', fmtMoney(unpaid.tienPhong) + ' đ');
  setText('rc-elec', fmtMoney(unpaid.tienDien) + ' đ');
  setText('rc-water', fmtMoney(unpaid.tienNuoc) + ' đ');
  setText('rc-other', fmtMoney(unpaid.phiKhac) + ' đ');
  setText('rc-total', fmtMoney(unpaid.tongTien));
  const paid = isPaidInvoice(unpaid);
  setText('rc-code', `MÃ SỐ BẢNG KÊ: HD-${String(unpaid.kyCuoc || '').replace('-', '')}-${unpaid.phongId || unpaid.id}`);
  setText('rc-title', `Chi tiết quyết toán cước tháng ${fmtKyCuoc(unpaid.kyCuoc)}`);
  setTag('rc-trang_thai-tag', paid, 'Đã thanh toán', 'Chưa thanh toán');
  setTag('rc-due-tag', paid, 'Đã thu', 'Chưa thu');

  // Khối Tổng quan — tiêu thụ điện, nước, hạn hợp đồng, và chỉ số kỳ này
  // csOverride = chỉ số trang 1 lấy từ request riêng; mặc định = tenantChiSo (khi đang ở trang 1).
  const cs = csOverride === undefined ? tenantChiSo : csOverride;
  setText('ov-elec', cs ? fmtMoney(cs.dienMoi - cs.dienCu) : '—');
  // Nước đổi đơn vị theo hình thức: theo khối (m³) hoặc theo đầu người.
  if (cs) {
    const per = String(cs.hinhThucNuoc || '').toLowerCase() === 'nguoi';
    setText('ov-nuoc', fmtMoney(per ? cs.soNguoiNuoc : cs.nuocMoi - cs.nuocCu));
    setText('ov-nuoc-unit', per ? 'người' : 'khối');
    setText('ov-nuoc-sub', `${per ? `${fmtMoney(cs.soNguoiNuoc)} người × ${fmtMoney(cs.giaNuoc)} đ` : `${fmtMoney(cs.nuocCu)} → ${fmtMoney(cs.nuocMoi)} khối × ${fmtMoney(cs.giaNuoc)} đ`}`);
    setText('ov-elec-sub', `${fmtMoney(cs.dienCu)} → ${fmtMoney(cs.dienMoi)} kWh × ${fmtMoney(cs.giaDien)} đ`);
    setText('rc-elec-sub', `${fmtMoney(cs.dienCu)} → ${fmtMoney(cs.dienMoi)} kWh × ${fmtMoney(cs.giaDien)} đ`);
    // rc-water-sub dùng định dạng khác: theo hinhThucNuoc (đầu người hoặc khối)
    setText('rc-water-sub', per ? `${fmtMoney(cs.soNguoiNuoc)} người × ${fmtMoney(cs.giaNuoc)} đ` : `${fmtMoney(cs.nuocCu)} → ${fmtMoney(cs.nuocMoi)} khối × ${fmtMoney(cs.giaNuoc)} đ`);
  } else {
    setText('ov-nuoc', '—');
    setText('ov-nuoc-unit', 'khối');
    setText('ov-elec-sub', 'Chưa có chỉ số kỳ này');
    setText('ov-nuoc-sub', 'Chưa có chỉ số kỳ này');
    setText('rc-elec-sub', 'Chưa có chỉ số kỳ này');
    setText('rc-water-sub', 'Chưa có chỉ số kỳ này');
  }

  // Khối Tổng quan — hợp đồng & hạn
  if (tenantHopDong) {
    setText('tcontract-due', `HĐ đến ${fmtDateOnly(tenantHopDong.ngayKetThuc)} · ${fmtMoney(tenantHopDong.giaThue)} đ/tháng`);
  } else {
    setText('tcontract-due', 'Chưa có hợp đồng hiệu lực');
  }
  setText('ov-month', fmtKyCuoc(unpaid.kyCuoc));
  setText('ov-total', fmtMoney(unpaid.tongTien) + ' đ');
  setText('ov-trang_thai', paid ? 'Đã thanh toán' : 'Chưa nộp');
  setText('due-alert-text', paid
    ? `Kỳ cước ${fmtKyCuoc(unpaid.kyCuoc)} đã được thanh toán. Cảm ơn bạn!`
    : `Kỳ cước ${fmtKyCuoc(unpaid.kyCuoc)} chưa hoàn tất thanh toán. Tổng cần nộp: ${fmtMoney(unpaid.tongTien)} đ.`);
  setTag('due-alert-tag', paid, 'Đã nộp', 'Chưa nộp');
}

function setTag(id, ok, okText, badText) {
  const el = document.getElementById(id);
  if (!el) return;
  el.textContent = ok ? okText : badText;
  el.className = ok == null ? 'tag' : `tag ${ok ? 'paid' : 'unpaid'}`;
}

function showInvoiceDetail(id) {
  const target = (currentTenantInvoices || []).find(i => Number(i.id) === Number(id));
  if (!target) return;
  const paid = isPaidInvoice(target);
  if (typeof openModal === 'function') {
    openModal({
      title: `Chi tiết kỳ cước ${fmtKyCuoc(target.kyCuoc)}`,
      fields: [
        { name: 'room', label: 'Tiền phòng', type: 'text', value: fmtMoney(target.tienPhong) + ' đ', disabled: true },
        { name: 'elec', label: 'Tiền điện', type: 'text', value: fmtMoney(target.tienDien) + ' đ', disabled: true },
        { name: 'water', label: 'Tiền nước', type: 'text', value: fmtMoney(target.tienNuoc) + ' đ', disabled: true },
        { name: 'other', label: 'Dịch vụ khác', type: 'text', value: fmtMoney(target.phiKhac) + ' đ', disabled: true },
        { name: 'total', label: 'Tổng cộng', type: 'text', value: fmtMoney(target.tongTien) + ' đ', disabled: true },
        { name: 'trang_thai', label: 'Trạng thái', type: 'text', value: paid ? `Đã thanh toán (${fmtDate(target.ngayDong)})` : 'Chưa thanh toán', disabled: true }
      ],
      onSubmit: async () => { /* chỉ để xem */ }
    });
  }
}

function switchTenantTab(name) {
  tenantActiveTab = name;
  syncTabBar('tenantTabs', name);
  document.querySelectorAll('.tenant-content > .tab-pane').forEach(p => {
    p.classList.toggle('active', p.id === `tab-${name}`);
  });
}

/**
 * Tab "Tiện ích": 4 KPI + biểu đồ SVG cột đôi tính từ dữ liệu đã nạp trong currentTenantInvoices.
 * Ruling A — MỌI KPI đều gắn sub ghi phạm vi, vì mảng chỉ là 1 trang: số kỳ → `trên N trang`,
 * còn lại → `tính trên M kỳ trang này`.
 */
// Kỳ cước 'yyyy-MM' → 'MM/yyyy'; giá trị rỗng/không khớp → '—'.
// Dùng chung cho KPI Tiện ích và nhãn trục biểu đồ — một nguồn định dạng, không nhân bản.
function fmtKyCuoc(v) {
  const s = String(v || '').trim();
  return /^\d{4}-\d{2}$/.test(s) ? s.slice(5) + '/' + s.slice(0, 4) : '—';
}

function renderTenantUtilities() {
  const kpis = document.getElementById('util-kpis');
  const charts = document.getElementById('util-charts');
  if (!kpis || !charts) return;
  const rows = currentTenantInvoices || [];
  const waterUnit = String(tenantChiSo?.hinhThucNuoc).toLowerCase() === 'nguoi' ? 'người' : 'm³';
  document.querySelectorAll('[data-water-unit]').forEach(el => { el.textContent = rows.length ? `Nước (${waterUnit})` : 'Nước'; });
  const n = rows.length;
  const totalPages = Math.max(1, Math.ceil(tenantHistoryTotal / tenantHistoryPageSize));
  if (!n) {
    // Ruling A: sub phạm vi vẫn đúng khi 0 kỳ — không bơm số giả.
    kpis.innerHTML = `<p class="empty-state">Chưa có kỳ cước trên trang này để tổng hợp.</p>`;
    charts.innerHTML = '<div class="chart-empty">Biểu đồ sẽ xuất hiện khi có hóa đơn.</div>';
    const ovEmpty = document.getElementById('ov-chart');
    if (ovEmpty) ovEmpty.innerHTML = '<div class="chart-empty">Biểu đồ sẽ xuất hiện khi có hóa đơn.</div>';
    return;
  }
  const sum = rows.reduce((a, h) => a + Number(h.tongTien || 0), 0);
  const avg = Math.round(sum / n);
  const top = rows.reduce((a, b) => Number(b.tongTien || 0) > Number(a.tongTien || 0) ? b : a, rows[0]);
  const paidSum = rows.filter(isPaidInvoice).reduce((a, h) => a + Number(h.tongTien || 0), 0);
  kpis.innerHTML = `
    <div class="kpi"><div class="lbl">Số kỳ đã phát sinh</div><div class="val">${tenantHistoryTotal}</div><div class="sub">trên ${totalPages} trang</div></div>
    <div class="kpi"><div class="lbl">Trung bình mỗi kỳ</div><div class="val">${fmtMoney(avg)} đ</div><div class="sub">tính trên ${n} kỳ trang này</div></div>
    <div class="kpi"><div class="lbl">Kỳ cao nhất</div><div class="val">${fmtMoney(top.tongTien)} đ</div><div class="sub">${esc(fmtKyCuoc(top.kyCuoc))} · tính trên ${n} kỳ trang này</div></div>
    <div class="kpi green"><div class="lbl">Tổng đã nộp</div><div class="val">${fmtMoney(paidSum)} đ</div><div class="sub">chỉ kỳ đã thu · ${n} kỳ trang này</div></div>`;
  charts.innerHTML = '';
  charts.appendChild(buildTenantChart(rows));
  // Khe "Xu hướng cước" ở Tổng quan — cùng dữ liệu, dựng lại SVG (không di chuyển node).
  const ov = document.getElementById('ov-chart');
  if (ov) { ov.innerHTML = ''; ov.appendChild(buildTenantChart(rows)); }
}

/**
 * Biểu đồ SVG cột đôi (điện amber #E0AF68: kWh, nước sage #8BD7A3: khối/người) theo kyCuoc tăng dần.
 * Vẽ bằng document.createElementNS — không thư viện (test TenantJs_BuildsChartWithoutLibrary).
 * Controller ruling: vẽ THEO SẢN LƯỢNG (kWh và khối), không vẽ theo tiền — tránh việc tiền nước
 * (~20k) bị tiền điện (~175k) đè bẹp khiến cột nước gần như tàng hình trên cùng một thang đo.
 * Quy đổi: tiền / đơn giá (lấy từ chiSoKyNay hiện có, hoặc đơn giá chuẩn 3.500 đ/kWh, 12.000 đ/khối).
 * Thang đo riêng cho từng chuỗi (chuẩn hoá theo max của chuỗi đó) để cả hai luôn nhìn thấy được.
 * ponytail: sản lượng suy từ tiền nên sai lệch nếu đơn giá đổi giữa các kỳ — chuyển sang đọc
 * chỉ số thật theo từng dòng khi server trả kèm chi_so trong danh sách hóa đơn.
 */
function buildTenantChart(rows) {
  const NS = 'http://www.w3.org/2000/svg';
  const data = [...rows].sort((a, b) => String(a.kyCuoc).localeCompare(String(b.kyCuoc))).slice(-12);
  const svg = document.createElementNS(NS, 'svg');
  svg.setAttribute('class', 'bar-chart');
  svg.setAttribute('role', 'img');

  const cs = tenantChiSo;
  const giaD = (cs && Number(cs.giaDien)) || 3500;
  const giaN = (cs && Number(cs.giaNuoc)) || 12000;
  const perNguoi = cs && String(cs.hinhThucNuoc || '').toLowerCase() === 'nguoi';
  const donViNuoc = perNguoi ? 'người' : 'khối';
  // Sản lượng ước tính từ hóa đơn — làm tròn về số nguyên
  const series = data.map(h => ({
    kyCuoc: h.kyCuoc,
    elecQty: Math.max(0, Math.round(Number(h.tienDien || 0) / giaD)),
    waterQty: Math.max(0, Math.round(Number(h.tienNuoc || 0) / giaN))
  }));

  const totE = series.reduce((a, s) => a + s.elecQty, 0);
  const totW = series.reduce((a, s) => a + s.waterQty, 0);
  const title = document.createElementNS(NS, 'title');
  title.textContent = `${data.length} kỳ từ ${fmtKyCuoc(data[0].kyCuoc)} đến ${fmtKyCuoc(data[data.length - 1].kyCuoc)}: tổng ${fmtMoney(totE)} kWh điện, ${fmtMoney(totW)} ${donViNuoc} nước`;
  svg.appendChild(title);

  const W = 320, H = 120, pad = 18;
  svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
  const maxE = Math.max(1, ...series.map(s => s.elecQty));
  const maxW = Math.max(1, ...series.map(s => s.waterQty));
  const gw = (W - pad * 2) / series.length;

  series.forEach((s, i) => {
    // Kỳ không tiêu thụ vẫn để 1px — cột 0px biến mất khỏi trục, đọc thành "thiếu dữ liệu".
    const eH = Math.max(1, Math.round(s.elecQty / maxE * (H - pad * 2)));
    const wH = Math.max(1, Math.round(s.waterQty / maxW * (H - pad * 2)));
    const x = pad + i * gw;

    const e = document.createElementNS(NS, 'rect');
    e.setAttribute('class', 'seg');
    e.setAttribute('x', x + 1); e.setAttribute('y', H - pad - eH);
    e.setAttribute('width', Math.max(1, gw / 2 - 2)); e.setAttribute('height', eH);
    e.setAttribute('fill', '#E0AF68');
    e.appendChild(segTitle(NS, `${fmtKyCuoc(s.kyCuoc)} — điện ${fmtMoney(s.elecQty)} kWh`));
    svg.appendChild(e);

    const w = document.createElementNS(NS, 'rect');
    w.setAttribute('class', 'seg');
    w.setAttribute('x', x + gw / 2); w.setAttribute('y', H - pad - wH);
    w.setAttribute('width', Math.max(1, gw / 2 - 2)); w.setAttribute('height', wH);
    w.setAttribute('fill', '#8BD7A3');
    w.appendChild(segTitle(NS, `${fmtKyCuoc(s.kyCuoc)} — nước ${fmtMoney(s.waterQty)} ${donViNuoc}`));
    svg.appendChild(w);

    if (series.length <= 4 || i === 0 || i === Math.floor((series.length - 1) / 2) || i === series.length - 1) {
      const t = document.createElementNS(NS, 'text');
      t.setAttribute('x', x + gw / 2); t.setAttribute('y', H - 5);
      t.setAttribute('text-anchor', 'middle'); t.setAttribute('font-size', '8'); t.setAttribute('fill', '#767E88');
      t.textContent = fmtKyCuoc(s.kyCuoc);
      svg.appendChild(t);
    }
  });
  return svg;
}

function segTitle(NS, text) {
  const t = document.createElementNS(NS, 'title');
  t.textContent = text;
  return t;
}

// Cấu hình nhận tiền của cơ sở (chủ trọ đặt ở tab Cài đặt). Nạp 1 lần khi mở shell.
let tenantCauHinh = null;
let tenantPaymentConfigError = false;

/**
 * Nạp cấu hình nhận tiền rồi vẽ khối QR chuyển khoản ở tab "Kỳ này".
 * Chưa cấu hình → ẩn khối QR, chỉ còn nút sao chép nội dung kỳ cước.
 */
async function loadPaymentConfig() {
  try {
    const cfg = await window.bridge.call('CAU_HINH_LAY', {});
    const bank = cfg?.tenNganHang ?? cfg?.nganHang;
    tenantCauHinh = bank && cfg.soTaiKhoan ? {
      nganHang: bank, soTaiKhoan: cfg.soTaiKhoan, chuTaiKhoan: cfg.chuTaiKhoan,
      templateQr: cfg.mauQr ?? cfg.templateQr
    } : null;
    tenantPaymentConfigError = false;
  } catch {
    tenantPaymentConfigError = true;
    tenantCauHinh = null;
  }
  renderPaymentBlock();
}

function hoaDonDangNo() {
  return tenantLatestInvoice && !isPaidInvoice(tenantLatestInvoice) ? tenantLatestInvoice : null;
}

function renderPaymentBlock() {
  const box = document.getElementById('pay-qr');
  if (!box) return;
  const cfg = tenantCauHinh;
  const hoaDon = hoaDonDangNo();
  const unavailable = tenantLoadError || tenantPaymentConfigError;
  const copy = document.getElementById('btn-copy-transfer');
  if (copy) copy.disabled = unavailable || !hoaDon;
  if (unavailable) {
    box.innerHTML = '<p class="pay-empty">Chưa tải được thông tin thanh toán. Kiểm tra kết nối và thử lại.</p>';
    return;
  }
  if (!hoaDon) {
    box.innerHTML = '<p class="pay-empty">Không có khoản cần thanh toán cho kỳ cước gần nhất.</p>';
    return;
  }
  if (!cfg) {
    box.innerHTML = '<p class="pay-empty">Chưa có tài khoản nhận tiền. Vui lòng liên hệ chủ trọ.</p>';
    return;
  }

  const url = sepayQrUrl(cfg, Number(hoaDon.tongTien) || 0, noiDungChuyenKhoan(hoaDon));
  box.innerHTML = `
    <img class="pay-qr-img" src="${esc(url)}" alt="Mã QR chuyển khoản ${esc(cfg.nganHang)}"
         onerror="this.replaceWith(Object.assign(document.createElement('p'),{className:'pay-empty',textContent:'Không tải được ảnh QR. Bạn vẫn có thể dùng thông tin bên dưới.'}))">
    <div class="pay-info">
      <div class="pay-row"><span>Kỳ chuyển khoản</span><b>${esc(fmtKyCuoc(hoaDon.kyCuoc))}</b></div>
      <div class="pay-row"><span>Ngân hàng</span><b>${esc(cfg.nganHang)}</b></div>
      <div class="pay-row"><span>Số tài khoản</span><b class="mono">${esc(cfg.soTaiKhoan)}</b></div>
      <div class="pay-row"><span>Chủ tài khoản</span><b>${esc(cfg.chuTaiKhoan || '—')}</b></div>
      <div class="pay-row"><span>Số tiền</span><b class="amount">${fmtMoney(hoaDon.tongTien)} đ</b></div>
      <div class="pay-row"><span>Nội dung</span><b class="mono">${esc(noiDungChuyenKhoan(hoaDon))}</b></div>
      <p class="pay-hint">Quét mã bằng ứng dụng ngân hàng hoặc chuyển khoản đúng nội dung để chủ trọ đối chiếu.</p>
    </div>`;
}

// Nội dung chuyển khoản: "<SoPhong> <KyCuoc>" bỏ dấu — SePay không nhận dấu và dấu '/'.
function noiDungChuyenKhoan(hoaDon) {
  const phong = String(tenantRoomName || hoaDon.soPhong || '').trim();
  const ky = String(hoaDon.kyCuoc || '').replace('/', '-');
  return [phong, ky].filter(Boolean).join(' ');
}

/**
 * Ruling B — copy thông tin chuyển khoản: <soPhong> <kyCuoc>: <tongTien> đ.
 * Có cấu hình nhận tiền thì chép kèm STK; chưa có thì chép dòng kỳ cước + ghi chú,
 * KHÔNG render placeholder STK nào lên màn hình.
 */
/**
 * Khách tự kiểm tra thanh toán. KHÔNG gọi DOI_SOAT_CHAY (action của Chủ trọ — mỗi lần gọi là
 * một request SePay, và khách không có quyền). Chỉ nạp lại hóa đơn của chính mình: nếu server
 * đã đối soát xong thì trạng thái Đã thu hiện ra ngay.
 */
async function kiemTraThanhToan() {
  const btn = document.getElementById('btn-kiem-tra');
  const note = document.getElementById('pay-check-note');
  const truoc = hoaDonDangNo()?.id ?? null;

  if (btn) btn.disabled = true;
  if (note) note.textContent = 'Đang kiểm tra...';
  try {
    await loadTenantInvoices();
    if (tenantLoadError) throw new Error('Chưa tải được dữ liệu để xác nhận thanh toán.');
    const conNo = hoaDonDangNo();
    const confirmed = truoc !== null && tenantLatestInvoice?.id === truoc && isPaidInvoice(tenantLatestInvoice);
    if (note) {
      note.textContent = confirmed
        ? 'Đã ghi nhận thanh toán. Cảm ơn bạn!'
        : (truoc !== null && conNo?.id === truoc)
          ? 'Chưa thấy chuyển khoản. Hệ thống tự kiểm tra lại mỗi phút.'
          : 'Đã tải lại dữ liệu. Chưa có xác nhận thanh toán mới cho kỳ đã chọn.';
    }
    if (confirmed) toast('Đã ghi nhận thanh toán.', 'ok');
  } catch (err) {
    if (note) note.textContent = 'Không kiểm tra được, thử lại sau.';
    toast(err.message, 'err');
  } finally {
    if (btn) btn.disabled = false;
  }
}

async function copyTransfer() {
  const target = tenantLoadError || tenantPaymentConfigError ? null : hoaDonDangNo();
  if (!target) {
    if (typeof toast === 'function') toast('Không có hóa đơn để sao chép', 'warn');
    return;
  }
  const line = `${noiDungChuyenKhoan(target)}: ${fmtMoney(target.tongTien)} đ`;
  const cfg = tenantCauHinh;
  const coCauHinh = !!cfg;
  const stk = coCauHinh ? `${cfg.nganHang} ${cfg.soTaiKhoan}${cfg.chuTaiKhoan ? ' — ' + cfg.chuTaiKhoan : ''}\n` : '';
  const hint = coCauHinh ? '' : ' (thông tin nhận tiền chưa cấu hình)';
  // hint phải vào clipboard: toast biến mất sau 4s, còn clipboard mới là thứ khách dán vào app ngân hàng.
  const text = `${stk}${line}${hint}`;
  try {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      await navigator.clipboard.writeText(text);
      if (typeof toast === 'function') toast(`Đã sao chép: ${line}${hint}`, 'ok');
    } else {
      if (typeof toast === 'function') toast(`Nội dung chuyển khoản: ${text}`, 'info');
    }
  } catch {
    if (typeof toast === 'function') toast(`Nội dung: ${text}`, 'info');
  }
}

// logout() khai báo ở shared/js/ui.js, tải trước file này.
document.addEventListener('DOMContentLoaded', () => {
  // auth.js điều hướng sang đây kèm ?u=<tên đã encode>.
  const params = new URLSearchParams(window.location.search);
  const name = params.get('u');
  if (name) {
    const card = document.getElementById('tname-card');
    if (card) card.textContent = name;
    const legacy = document.getElementById('tname');
    if (legacy) legacy.textContent = name;
    setText('tavatar', name.trim().split(/\s+/).slice(-2).map(part => part[0]).join('').toUpperCase());
  }
  switchTenantTab(params.get('tab') === 'history' ? 'history' : 'overview');
  wireTabKeys('tenantTabs', switchTenantTab);
  wireTabKeys('checkinTabs', switchCheckinTab);
  document.querySelectorAll('#tenantTabs [data-tab]').forEach(btn => {
    btn.addEventListener('click', () => switchTenantTab(btn.dataset.tab));
  });
  document.querySelectorAll('#checkinTabs [data-tab]').forEach(btn => {
    btn.addEventListener('click', () => switchCheckinTab(btn.dataset.tab));
  });
  document.getElementById('qrFile')?.addEventListener('change', e => decodeQrFile(e.target));
  document.getElementById('btn-cam')?.addEventListener('click', toggleCamera);
  document.getElementById('qrPin')?.addEventListener('keydown', e => {
    if (e.key === 'Enter') { e.preventDefault(); submitPin(); }
  });
  document.getElementById('btnShowCheckin')?.addEventListener('click', () => showCheckinScreen(true));
  document.getElementById('btnTenantCheckinSubmit')?.addEventListener('click', submitPin);
  document.getElementById('btnBackToOverview')?.addEventListener('click', () => showCheckinScreen(false));
  document.getElementById('btnTenantReportIssue')?.addEventListener('click', openTenantReportIssueModal);
  document.addEventListener('keydown', e => {
    if (e.key === 'Escape' && document.getElementById('appModal')?.classList.contains('open')) {
      UIChrome.closeModal();
      document.getElementById('btnTenantReportIssue')?.focus();
    }
  });
  window.addEventListener('pagehide', stopCamera);
  loadTenantInvoices();
  // ponytail: tự động làm mới toàn trang mỗi 30s + khi quay lại cửa sổ — thay nút sync tay.
  startAutoRefresh(loadTenantInvoices, 30000);
});

// M4b: nút "Báo sự cố" của khách thuê gọi SU_CO_TAO (server tự xác định phòng từ session).
function openTenantReportIssueModal() {
  if (tenantRoomId === null) {
    if (typeof UIChrome !== 'undefined' && UIChrome.showToast) {
      UIChrome.showToast('Bạn chưa nhận phòng nên chưa thể báo sự cố.');
    } else if (typeof toast === 'function') {
      toast('Bạn chưa nhận phòng nên chưa thể báo sự cố.', 'warn');
    }
    return;
  }
  const html = `
    <p class="tenant-muted">Mô tả rõ vị trí và tình trạng để nhân viên hỗ trợ đúng việc.</p>
    <div class="field">
      <label for="kt-sc-mucDo">Mức độ ưu tiên</label>
      <select id="kt-sc-mucDo" class="select">
        <option value="Thap">Thấp</option>
        <option value="TrungBinh" selected>Trung bình</option>
        <option value="Cao">Cao</option>
        <option value="KhanCap">Khẩn cấp</option>
      </select>
    </div>
    <div class="field">
      <label for="kt-sc-moTa">Mô tả sự cố</label>
      <textarea id="kt-sc-moTa" class="textarea" rows="4" maxlength="1000" placeholder="VD: Vòi nước bị rò rỉ, bóng đèn hỏng..."></textarea>
    </div>
    <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:12px;">
      <button type="button" class="btn btn-secondary btn-sm" id="kt-sc-cancel">Hủy</button>
      <button type="button" class="btn btn-primary btn-sm" id="kt-sc-submit">Gửi báo cáo</button>
    </div>`;
  UIChrome.openModal('Báo sự cố phòng', html);
  document.getElementById('kt-sc-moTa')?.focus();
  document.getElementById('kt-sc-cancel')?.addEventListener('click', () => UIChrome.closeModal());
  const submitBtn = document.getElementById('kt-sc-submit');
  submitBtn?.addEventListener('click', async () => {
    const mucDo = document.getElementById('kt-sc-mucDo')?.value || 'TrungBinh';
    const moTa = (document.getElementById('kt-sc-moTa')?.value || '').trim();
    if (!moTa) {
      UIChrome.showToast('Vui lòng nhập mô tả sự cố.');
      return;
    }
    try {
      submitBtn.disabled = true;
      submitBtn.textContent = 'Đang gửi…';
      await window.bridge.call('SU_CO_TAO', { mucDo, moTa });
      UIChrome.closeModal();
      UIChrome.showToast('Đã gửi báo cáo sự cố thành công.');
    } catch (err) {
      UIChrome.showToast('Lỗi: ' + err.message);
      submitBtn.disabled = false;
      submitBtn.textContent = 'Gửi báo cáo';
    }
  });
}
