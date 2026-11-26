// Run with: node --test datlich-stock.test.cjs
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { runInNewContext } = require('node:vm');

const root = join(__dirname, '..');
const html = readFileSync(join(root, 'datlich.html'), 'utf8');
const room = { phongId: 11, soPhong: '103', tenKhu: 'Khu A', giaThue: 2500000 };
const other = { ...room, phongId: 12, soPhong: '106' };
const success = data => ({ success: true, data });
const settle = () => new Promise(resolve => setImmediate(resolve));

async function openPage(initial) {
  const elements = {}, listeners = {}, timers = [], bookings = [];
  const doc = {
    addEventListener: (type, handler) => { listeners[type] = handler; }, querySelector: () => null,
    getElementById: id => elements[id] ??= {
      value: '', textContent: '', dataset: {}, disabled: false, tagName: id === 'bookPhong' ? 'SELECT' : 'DIV',
      contains(el) { return el === this; },
      addEventListener(type, handler) { this[type] = handler; },
      replaceChildren(...opts) { this.options = opts; this.value = ''; },
      add(opt) { this.options.push(opt); },
    },
  };
  doc.getElementById('bookSdt');
  const page = { read: () => success(initial), loads: 0, elements, doc, listeners, timers, bookings };
  const ctx = { document: doc, requestAnimationFrame: setImmediate, setInterval: (tick, ms) => timers.push({ tick, ms }),
    Option: class { constructor(text, value) { this.text = text; this.value = String(value); } },
    window: { QuanLyTroBridge: { async invoke(action, payload) {
      if (action === 'PHONG_LAY_TRONG_PUBLIC') { page.loads++; return page.read(); }
      assert.equal(action, 'KHACH_THUE_DAT_LICH'); bookings.push({ ...payload }); return success({});
    } } },
  };
  for (const [, src, inline] of html.matchAll(/<script\b(?:\s+src="([^"]+)")?[^>]*>([\s\S]*?)<\/script>/g)) {
    if (!src || src.startsWith('shared/js/ui.js')) runInNewContext(src ? readFileSync(join(root, src.split('?')[0]), 'utf8') : inline, ctx);
  }
  await settle();
  return page;
}

const ids = e => e.bookPhong.options.map(o => o.value).filter(Boolean);

test('Chỉ cho đặt phòng do máy chủ trả về', async () => {
  const [, initialOptions] = html.match(/<select\b[^>]*\bid="bookPhong"[^>]*>([\s\S]*?)<\/select>/);
  assert.doesNotMatch(initialOptions, /<option\b[^>]*\bvalue="[^"]+"/);
  const p = await openPage([]), e = p.elements;
  assert.deepEqual(ids(e), []);
  assert.equal(e.bookingFields.disabled, true);
  const original = e.roomStateTitle.textContent;
  p.read = () => { throw new Error('Mất kết nối'); };
  await e.retryVacantRooms.click();
  assert.deepEqual(ids(e), []);
  assert.equal(e.bookingFields.disabled, true);
  assert.notEqual(e.roomStateTitle.textContent + (e.roomRefreshWarning?.textContent || ''), original);
  p.read = () => success([room]);
  await e.retryVacantRooms.click();
  assert.deepEqual(ids(e), ['11']);
  assert.equal(e.bookingFields.disabled, false);
});

test('Cập nhật 30 giây giữ bản nhập và phòng đã chọn; lỗi giữ danh sách tốt gần nhất', async () => {
  const p = await openPage([room]), e = p.elements;
  assert.deepEqual(p.timers.map(t => t.ms), [30000], 'Trang phải đăng ký đúng một vòng làm mới 30 giây');
  const { tick } = p.timers[0];
  const time = e.bookThoiGian.value;
  e.bookHoTen.value = 'Dữ liệu kiểm thử';
  e.bookSdt.value = 'SĐT kiểm thử';
  e.bookPhong.value = '11';
  const options = e.bookPhong.options;
  await tick();
  assert.equal(e.bookPhong.options, options, 'Không dựng lại danh sách khi không có thay đổi');
  p.read = () => success([room, other]);
  await tick();
  assert.deepEqual(ids(e), ['11', '12']);
  assert.equal(e.bookPhong.value, '11');
  assert.match(e.roomsUpdated.textContent, /Cập nhật lần cuối \d{2}:\d{2}/);
  const timestamp = e.roomsUpdated.textContent;
  p.read = () => success([{ ...room, giaThue: null }]);
  await tick();
  assert.equal(e.roomsUpdated.textContent, timestamp);
  assert.match(e.roomRefreshWarning.textContent, /chưa cập nhật|chưa thể cập nhật/i);
  assert.equal(e.bookingFields.disabled, false);
  assert.equal(e.bookPhong.value, '11');
  assert.deepEqual(ids(e), ['11', '12']);
  assert.equal(e.bookHoTen.value, 'Dữ liệu kiểm thử');
  assert.equal(e.bookSdt.value, 'SĐT kiểm thử');
  assert.equal(e.bookThoiGian.value, time);
  p.read = () => success([other]);
  await e.retryVacantRooms.click();
  assert.deepEqual(ids(e), ['12']);
  assert.equal(e.bookPhong.value, '');
  assert.match(e.bookingFormNote.textContent, /không còn trống/i);
  assert.equal(e.roomRefreshWarning.textContent, '');
});

test('Phòng cuối vừa hết trống không ngắt ô đang nhập khi phản hồi về', async () => {
  const p = await openPage([room]), e = p.elements;
  let finish;
  p.read = () => new Promise(resolve => { finish = resolve; });
  const request = p.timers[0].tick();
  e.bookHoTen.value = 'Tên đang nhập';
  p.doc.activeElement = e.bookHoTen;
  e.bookingFields.contains = el => el === e.bookHoTen;
  finish(success([])); await request;
  assert.equal(e.bookingFields.disabled, false, 'Khóa fieldset sẽ làm mất focus của ô đang nhập');
  p.doc.activeElement = null;
  p.listeners.focusout(); await settle();
  assert.equal(e.bookingFields.disabled, true);
  assert.deepEqual(ids(e), []);
  assert.equal(e.bookHoTen.value, 'Tên đang nhập');
});

test('Nút tải lại giữ focus trong lúc tải nền; mốc cập nhật chờ danh sách hiển thị', async () => {
  const p = await openPage([room]), e = p.elements;
  let finish;
  p.read = () => new Promise(resolve => { finish = resolve; });
  const request = p.timers[0].tick();
  assert.equal(e.retryVacantRooms.disabled, false, 'Không disable nút đang được focus khi polling');
  p.doc.activeElement = e.bookPhong;
  e.roomsUpdated.textContent = 'Mốc đang hiển thị';
  finish(success([other])); await request;
  assert.equal(e.roomsUpdated.textContent, 'Mốc đang hiển thị');
  p.doc.activeElement = null;
  p.listeners.focusout(); await settle();
  assert.match(e.roomsUpdated.textContent, /Cập nhật lần cuối/);
});

test('Chọn lại phòng hợp lệ xóa cảnh báo phòng cũ không còn trống', async () => {
  const p = await openPage([room]), e = p.elements;
  e.bookPhong.value = '11';
  p.read = () => success([other]);
  await p.timers[0].tick();
  assert.match(e.bookingFormNote.textContent, /không còn trống/);
  e.bookPhong.value = '12';
  e.bookPhong.change?.();
  assert.doesNotMatch(e.bookingFormNote.textContent, /không còn trống/);
});

test('Phản hồi đang bay không đổi select đang mở, không gửi phòng vừa hết trống', async () => {
  const p = await openPage([room]), e = p.elements;
  assert.equal(p.timers.length, 1, 'Thiếu polling trên trang đặt lịch');
  const { tick } = p.timers[0];
  let resolve;
  p.read = () => new Promise(done => { resolve = done; });
  const pending = tick();
  await tick(); await e.retryVacantRooms.click();
  assert.equal(p.loads, 2, 'Lượt đầu và đúng một request đang bay');
  p.doc.activeElement = e.bookPhong;
  e.bookPhong.value = '11';
  resolve(success([other])); await pending;
  assert.deepEqual(ids(e), ['11']);
  assert.equal(e.bookPhong.value, '11');
  e.bookHoTen.value = e.bookSdt.value = 'test';
  await e.publicBookingForm.submit({ preventDefault() {} });
  assert.deepEqual(p.bookings, []);
  p.doc.activeElement = null;
  p.listeners.focusout(); await settle();
  assert.deepEqual(ids(e), ['12']);
  assert.equal(e.bookPhong.value, '');
  p.doc.hidden = true; await tick(); assert.equal(p.loads, 2);
  p.doc.hidden = false; p.read = () => success([room]);
  p.listeners.visibilitychange(); await settle();
  assert.deepEqual(ids(e), ['11']);
});
