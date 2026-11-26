const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { runInNewContext } = require('node:vm');

const root = join(__dirname, '..', 'khachthue');
const html = readFileSync(join(root, 'index.html'), 'utf8');
const js = readFileSync(join(root, 'js', 'khach_thue.js'), 'utf8');
const tags = [...html.matchAll(/<(\w+)\b[^>]*>/g)].map(([tag]) => tag);
const byId = id => tags.find(tag => tag.includes(`id="${id}"`)) || '';

test('Mỗi cách nhận phòng có tab và điều khiển tương ứng, không điền sẵn PIN', () => {
  for (const method of ['anh', 'camera', 'pin']) {
    const button = tags.find(tag => tag.startsWith('<button ') && tag.includes(`data-tab="${method}"`));
    assert.ok(button?.includes(`aria-controls="checkin-${method}"`), `Thiếu tab nhận phòng ${method}`);
    assert.ok(byId(`checkin-${method}`));
  }
  assert.match(byId('qrFile'), /type="file"/);
  assert.match(byId('qrCanvas'), /^<canvas\b/);
  assert.match(byId('qrVideo'), /^<video\b/);
  assert.match(byId('qrPin'), /maxlength="8"/);
  assert.doesNotMatch(byId('qrPin'), /value="[^"]+"/);
});

test('Chưa có hóa đơn không hiển thị tổng cước 0 đ', () => {
  const elements = {};
  const ctx = { document: { addEventListener() {}, getElementById(id) { return elements[id] ??= {}; } } };
  runInNewContext(js, ctx);
  ctx.renderTenantNoInvoice();
  assert.equal(elements['ov-total'].textContent, '—');
  assert.equal(elements['rc-total'].textContent, '—');
  assert.match(elements['rc-trang_thai-tag'].textContent, /Chưa có hóa đơn/);
});

test('Lỗi kết nối thay trạng thái đang tải ở tab Điện & Nước', () => {
  const loading = { innerHTML: '<p>Đang tải dữ liệu…</p>' };
  const ctx = { document: { addEventListener() {}, getElementById: id => id === 'util-kpis' ? loading : null } };
  runInNewContext(js, ctx);
  ctx.renderTenantEmpty();
  assert.match(loading.innerHTML, /Không tải được dữ liệu/);
  assert.doesNotMatch(loading.innerHTML, /Đang tải/);
});

test('Rời tab camera trong lúc chờ quyền thì không gắn luồng vào video', async () => {
  const video = { play: async () => {} };
  let allow;
  let stopped = false;
  const ctx = {
    document: { addEventListener() {}, getElementById: id => id === 'qrVideo' ? video : null },
    navigator: { mediaDevices: { getUserMedia: () => new Promise(resolve => { allow = resolve; }) } },
    alertDialog() {},
  };
  runInNewContext(js, ctx);
  const starting = ctx.toggleCamera();
  ctx.stopCamera();
  allow({ getTracks: () => [{ stop() { stopped = true; } }] });
  await starting;
  assert.equal(stopped, true);
  assert.equal(video.srcObject ?? null, null);
});

function paymentScreen(invoicesFor) {
  const ids = ['rc-total', 'rc-due-tag', 'rc-trang_thai-tag', 'pay-qr', 'btn-copy-transfer', 'pay-check-note', 'tenant-page-indicator', 'btnShowCheckin', 'btnBackToOverview'];
  const elements = Object.fromEntries(ids.map(id => [id, {}]));
  const clipboard = [], tones = [];
  const ctx = {
    document: { addEventListener() {}, getElementById: id => elements[id] ?? null },
    navigator: { clipboard: { writeText: text => clipboard.push(text) } }, URLSearchParams,
    window: { bridge: { call: async (action, { page, soLuongMoiTrang }) => {
      if (action === 'CAU_HINH_LAY') return { tenNganHang: 'BANK_TEST', soTaiKhoan: 'ACCOUNT_TEST', mauQr: 'compact' };
      assert.equal(action, 'HOA_DON_CUA_TOI');
      const rows = invoicesFor(page);
      return { danhSach: rows.slice((page - 1) * soLuongMoiTrang, page * soLuongMoiTrang), tongSo: rows.length, trang: page, soLuongMoiTrang, phongId: 1, soPhong: 'P01' };
    } } },
  };
  runInNewContext(readFileSync(join(root, '..', 'shared/js/ui.js'), 'utf8'), ctx);
  runInNewContext(js, ctx);
  ctx.toast = (_, tone) => tones.push(tone);
  return { ctx, elements, clipboard, tones };
}

const latestBill = { id: 17, kyCuoc: '2026-11', tongTien: 100000, trangThai: 'ChuaThu' };
const olderBill = { id: 16, kyCuoc: '2026-10', tongTien: 85000, trangThai: 'ChuaThu' };

test('QR chuyển khoản dùng đúng cấu hình công khai của máy chủ', async () => {
  const { ctx, elements } = paymentScreen(() => [latestBill]);
  await ctx.loadTenantInvoices();
  const qr = elements['pay-qr'].innerHTML;
  assert.match(qr, /class="pay-qr-img"/);
  assert.match(qr, /bank=BANK_TEST/);
  assert.match(qr, /acc=ACCOUNT_TEST/);
  assert.match(qr, /amount=100000/);
});

test('Chỉ xác nhận thanh toán khi dữ liệu mới ghi đã thu cho đúng kỳ', async () => {
  for (const [response, feedback] of [
    [() => { throw new Error('Mất kết nối'); }, /Không kiểm tra được/],
    [() => [], /Chưa có xác nhận/],
    [() => [{ ...latestBill, id: 42, trangThai: 'DaThu' }], /Chưa có xác nhận/],
    [() => [latestBill], /Chưa thấy chuyển khoản/],
    [() => [{ ...latestBill, trangThai: 'DaThu' }], /Đã ghi nhận/],
  ]) {
    let read = () => [latestBill];
    const { ctx, elements, tones } = paymentScreen(() => read());
    await ctx.loadTenantInvoices();
    read = response;
    await ctx.kiemTraThanhToan();
    assert.match(elements['pay-check-note'].textContent, feedback);
    assert.equal(tones.includes('ok'), feedback.test('Đã ghi nhận thanh toán.'));
  }
});

test('Sang trang lịch sử khác không đổi QR, nội dung sao chép hay kỳ xác nhận', async () => {
  const rows = [{ ...latestBill }, olderBill];
  const { ctx, elements, clipboard } = paymentScreen(() => rows);
  await ctx.loadTenantHistoryPage(1, 1);
  await ctx.loadTenantInvoices();
  await ctx.changeTenantPage(1);
  assert.equal(elements['tenant-page-indicator'].textContent, 'Trang 2 / 2');
  assert.equal(elements['rc-total'].textContent, '100.000');
  assert.match(elements['pay-qr'].innerHTML, /amount=100000/);
  assert.match(elements['pay-qr'].innerHTML, /P01\+2026\+11/);
  await ctx.copyTransfer();
  assert.match(clipboard[0], /P01 2026-11: 100\.000 đ/);

  rows[0] = { ...latestBill, trangThai: 'DaThu' };
  await ctx.kiemTraThanhToan();
  assert.match(elements['pay-check-note'].textContent, /Đã ghi nhận/);
  assert.equal(elements['rc-due-tag'].textContent, 'Đã thu');
  assert.doesNotMatch(elements['pay-qr'].innerHTML, /pay-qr-img/);
  assert.equal(elements['btn-copy-transfer'].disabled, true);
});

test('Không dùng hóa đơn lịch sử khi kỳ mới nhất trống hoặc lỗi tải', async () => {
  for (const [readLatest, status] of [
    [() => [], 'Chưa có hóa đơn'],
    [() => { throw new Error('Mất kết nối'); }, 'Chưa rõ'],
  ]) {
    let read = () => [latestBill, olderBill];
    const { ctx, elements, clipboard } = paymentScreen(page => read(page));
    await ctx.loadTenantHistoryPage(1, 1);
    await ctx.loadTenantInvoices();
    await ctx.changeTenantPage(1);
    read = page => page === 1 ? readLatest() : [latestBill, olderBill];
    await ctx.loadTenantInvoices();
    assert.equal(elements['rc-trang_thai-tag'].textContent, status);
    assert.doesNotMatch(elements['pay-qr'].innerHTML, /pay-qr-img/);
    assert.equal(elements['btn-copy-transfer'].disabled, true);
    await ctx.copyTransfer();
    assert.deepEqual(clipboard, []);
  }
});
