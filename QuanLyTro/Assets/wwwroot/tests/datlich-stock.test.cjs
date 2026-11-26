// Run from this directory with: node --test datlich-stock.test.cjs
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { runInNewContext } = require('node:vm');

test('Chỉ cho đặt phòng do máy chủ trả về', async () => {
  const html = readFileSync(join(__dirname, '..', 'datlich.html'), 'utf8');
  const [, initialOptions] = html.match(/<select\b[^>]*\bid="bookPhong"[^>]*>([\s\S]*?)<\/select>/);
  assert.doesNotMatch(initialOptions, /<option\b[^>]*\bvalue="[^"]+"/, 'Không có phòng đặt sẵn trước khi tải');

  const elements = {};
  const document = { getElementById: id => elements[id] ??= {
    addEventListener(event, handler) { this[event] = handler; },
    replaceChildren(...children) { this.options = children; },
    add(option) { this.options.push(option); }
  } };
  let load = async () => ({ success: true, data: [] });
  const bridge = { invoke(action) { assert.equal(action, 'PHONG_LAY_TRONG_PUBLIC'); return load(); } };
  const inline = [...html.matchAll(/<script\b[^>]*>([\s\S]*?)<\/script>/g)].map(match => match[1]).join('\n');
  runInNewContext(inline, { document, window: { QuanLyTroBridge: bridge }, Option: class { constructor(_, value) { this.value = value; } } });
  await new Promise(resolve => setImmediate(resolve));

  const roomIds = () => elements.bookPhong.options.map(option => option.value).filter(Boolean);
  assert.deepEqual(roomIds(), []);
  assert.equal(elements.bookingFields.disabled, true);
  const emptyMessage = elements.roomStateTitle.textContent;

  load = async () => { throw new Error('Không thể kết nối'); };
  await elements.retryVacantRooms.click();
  assert.deepEqual(roomIds(), []);
  assert.equal(elements.bookingFields.disabled, true);
  assert.notEqual(elements.roomStateTitle.textContent, emptyMessage);

  load = async () => ({ success: true, data: [{ phongId: 11, soPhong: '103', tenKhu: 'Khu A', giaThue: 2500000 }] });
  await elements.retryVacantRooms.click();
  assert.deepEqual(roomIds(), [11]);
  assert.equal(elements.bookingFields.disabled, false);
});
