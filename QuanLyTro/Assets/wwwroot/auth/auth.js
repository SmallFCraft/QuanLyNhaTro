// Đăng nhập + BR-17 đăng ký khách thuê. Bridge shim (chrome.js) giữ hợp đồng window.bridge.call.
const ROLE_ROUTES = {
  'ChuTro': '../chutro/index.html?vai_tro=ChuTro',
  'QuanLy': '../quanly/index.html',
  'LeTan': '../letan/index.html',
  'NhanVien': '../nhanvien/index.html',
  'KhachThue': '../khachthue/index.html',
  'CongAn': '../congan/index.html'
};

// Tài khoản demo của dự án: mật khẩu trùng tên đăng nhập.
const $ = id => document.getElementById(id);

function showError(id, msg) { $(id).textContent = msg || ''; }

function switchAuthTab(mode) {
  const reg = mode === 'register';
  $('tabLogin').classList.toggle('active', !reg);
  $('tabRegister').classList.toggle('active', reg);
  $('tabLogin').setAttribute('aria-selected', String(!reg));
  $('tabRegister').setAttribute('aria-selected', String(reg));
  $('panelLogin').hidden = reg;
  $('panelRegister').hidden = !reg;
  showError('loginError'); showError('registerError');
}

let regSubmitting = false;
async function doRegister() {
  if (regSubmitting) return;
  showError('registerError');
  const cccd = $('regCccd').value.trim();
  const matKhau = $('regMatKhau').value;
  const ngaySinh = $('regNgaySinh').value;
  if (!$('regHoTen').value.trim()) return showError('registerError', 'Vui lòng nhập họ tên.');
  if (!/^\d{12}$/.test(cccd)) return showError('registerError', 'Số CCCD phải gồm đúng 12 chữ số.');
  if (!ngaySinh) return showError('registerError', 'Vui lòng chọn ngày sinh.');
  if (!matKhau || matKhau.length < 6) return showError('registerError', 'Mật khẩu phải từ 6 ký tự trở lên.');

  const btn = $('btnRegister');
  regSubmitting = true; btn.disabled = true;
  try {
    await window.bridge.call('KHACH_THUE_DANG_KY', {
      HoTen: $('regHoTen').value.trim(),
      NgaySinh: ngaySinh,
      Cccd: cccd,
      SoDienThoai: $('regSdt').value.trim(),
      QueQuan: $('regQueQuan').value.trim(),
      NoiLamViec: $('regNoiLamViec').value.trim() || null,
      matKhau,
      tenDangNhap: cccd
    });
    $('loginUsername').value = cccd;
    $('loginPassword').value = '';
    $('regMatKhau').value = '';
    switchAuthTab('login');
    UIChrome.openModal('Thành công', '<p>Đăng ký thành công. Hãy đăng nhập để nhận phòng bằng mã QR/PIN của phòng.</p>');
  } catch (err) {
    showError('registerError', err.message);
  } finally {
    regSubmitting = false; btn.disabled = false;
  }
}

let loginSubmitting = false;
async function doLogin() {
  if (loginSubmitting) return;
  showError('loginError');
  const tenDangNhap = $('loginUsername').value.trim();
  const matKhau = $('loginPassword').value;
  if (!tenDangNhap || !matKhau) return showError('loginError', 'Vui lòng nhập tài khoản và mật khẩu.');
  const btn = $('btnLogin');
  loginSubmitting = true; btn.disabled = true;
  try {
    const user = await window.bridge.call('DANG_NHAP', { TenDangNhap: tenDangNhap, MatKhau: matKhau });
    if (!user) return;
    const target = ROLE_ROUTES[user.vaiTro];
    if (!target) return showError('loginError', 'Tài khoản chưa được gán vai trò hợp lệ. Vui lòng liên hệ Chủ trọ.');
    // Form1 tự navigate theo DANG_NHAP; href giữ cho preview ngoài WebView2.
    window.location.href = target + (target.includes('?') ? '&' : '?') + 'u=' + encodeURIComponent(user.hoTen ?? user.tenDangNhap ?? '');
  } catch (err) {
    showError('loginError', err.message);
  } finally {
    loginSubmitting = false; btn.disabled = false;
  }
}

document.addEventListener('DOMContentLoaded', () => {
  $('loginForm').addEventListener('submit', e => { e.preventDefault(); doLogin(); });
  $('formRegister').addEventListener('submit', e => { e.preventDefault(); doRegister(); });
  $('tabLogin').addEventListener('click', () => switchAuthTab('login'));
  $('tabRegister').addEventListener('click', () => switchAuthTab('register'));
  $('togglePwdBtn').addEventListener('click', () => {
    const p = $('loginPassword');
    p.type = p.type === 'password' ? 'text' : 'password';
  });
  $('demoAccountSelect').addEventListener('change', e => {
    const u = e.target.value;
    if (!u) return;
    $('loginUsername').value = u;
    $('loginPassword').value = u;
    showError('loginError');
    $('btnLogin').focus();
  });
});
