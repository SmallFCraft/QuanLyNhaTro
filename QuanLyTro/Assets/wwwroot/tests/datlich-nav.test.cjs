const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { runInNewContext } = require('node:vm');

const root = join(__dirname, '..');
const html = readFileSync(join(root, 'datlich.html'), 'utf8');

function openNav(scrollY = 0) {
  const src = html.match(/<script\b[^>]*src="(datlich-nav\.js)"/)?.[1];
  assert.ok(src, 'Trang đặt lịch chưa nạp điều hướng riêng');
  const handlers = new Map();
  const classes = new Set();
  const header = { classList: { add: name => classes.add(name), toggle: (name, on) => on ? classes.add(name) : classes.delete(name) } };
  const win = {
    scrollY,
    matchMedia: () => ({ matches: true, addEventListener() {} }),
    addEventListener: (name, fn) => handlers.set(name, fn),
  };
  const document = { querySelector: () => header };
  runInNewContext(readFileSync(join(root, src), 'utf8'), { window: win, document });
  return { classes, handlers, win };
}

test('Header không đổi kiểu liên tục khi cuộn quanh điểm chuyển', () => {
  const { classes, handlers, win } = openNav();
  const overHero = () => classes.has('is-over-hero');
  assert.equal(overHero(), true);
  for (const [y, expected] of [[47, true], [49, true], [46, true], [65, false], [49, false], [47, false], [27, true], [49, true]]) {
    win.scrollY = y;
    handlers.get('scroll')();
    assert.equal(overHero(), expected, `Header tại vị trí cuộn ${y}px`);
  }
  assert.equal(openNav(180).classes.has('is-over-hero'), false, 'Tải lại ở đoạn giữa trang phải có nền sáng');
});

test('Header không chiếm sự kiện cuộn của trình duyệt', () => {
  const { handlers } = openNav();
  assert.equal(handlers.has('wheel'), false, 'Cuộn con lăn không được chờ xử lý JavaScript');
});
