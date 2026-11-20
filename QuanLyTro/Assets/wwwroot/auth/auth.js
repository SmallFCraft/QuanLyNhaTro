// Đăng nhập + BR-17 đăng ký khách thuê. Bridge shim (chrome.js) giữ hợp đồng window.bridge.call.
const ROLE_ROUTES = {
  'ChuTro': '../chutro/index.html?vai_tro=ChuTro',
  'QuanLy': '../quanly/index.html',
  'LeTan': '../letan/index.html',
  'NhanVien': '../nhanvien/index.html',
  'KhachThue': '../khachthue/index.html',
  'CongAn': '../congan/index.html'
};

function alertDialog(msg, title, kind) { UIChrome.openModal(title || 'Thông báo', `<p>${msg}</p>`); }

function switchAuthTab(mode) {
  const reg = mode === 'register';
  document.getElementById('loginForm').style.display = reg ? 'none' : '';
  document.getElementById('formRegister').style.display = reg ? '' : 'none';
}

let regSubmitting = false;
async function doRegister() {
  if (regSubmitting) return;
  const cccd = document.getElementById('regCccd').value.trim();
  const matKhau = document.getElementById('regMatKhau').value;
  const ngaySinh = document.getElementById('regNgaySinh').value;
  if (!/^\d{12}$/.test(cccd)) return alertDialog('Số CCCD phải gồm đúng 12 chữ số.', 'Dữ liệu chưa hợp lệ', 'warn');
  if (!ngaySinh) return alertDialog('Vui lòng chọn ngày sinh.', 'Dữ liệu chưa hợp lệ', 'warn');
  if (!matKhau || matKhau.length < 6) return alertDialog('Mật khẩu phải từ 6 ký tự trở lên.', 'Dữ liệu chưa hợp lệ', 'warn');

  const btn = document.querySelector('#formRegister button[type="submit"]');
  regSubmitting = true; if (btn) btn.disabled = true;
  try {
    await window.bridge.call('KHACH_THUE_DANG_KY', {
      HoTen: document.getElementById('regHoTen').value.trim(),
      NgaySinh: ngaySinh,
      Cccd: cccd,
      SoDienThoai: document.getElementById('regSdt').value.trim(),
      QueQuan: document.getElementById('regQueQuan').value.trim(),
      NoiLamViec: document.getElementById('regNoiLamViec').value.trim() || null,
      matKhau,
      tenDangNhap: cccd
    });
    document.getElementById('loginUsername').value = cccd;
    document.getElementById('loginPassword').value = '';
    document.getElementById('regMatKhau').value = '';
    switchAuthTab('login');
    await alertDialog('Đăng ký thành công. Hãy đăng nhập để nhận phòng bằng mã QR/PIN của phòng.', 'Thành công', 'ok');
  } catch (err) {
    alertDialog(err.message, 'Đăng ký không thành công', 'err');
  } finally {
    regSubmitting = false; if (btn) btn.disabled = false;
  }
}

async function doLogin() {
  const tenDangNhap = document.getElementById('loginUsername').value.trim();
  const matKhau = document.getElementById('loginPassword').value;
  if (!tenDangNhap || !matKhau) return alertDialog('Vui lòng nhập tài khoản và mật khẩu.', 'Thiếu thông tin', 'warn');
  try {
    const user = await window.bridge.call('DANG_NHAP', { TenDangNhap: tenDangNhap, MatKhau: matKhau });
    if (!user) return;
    const target = ROLE_ROUTES[user.vaiTro];
    if (!target) return alertDialog('Tài khoản chưa được gán vai trò hợp lệ. Vui lòng liên hệ Chủ trọ.', 'Không thể truy cập', 'err');
    // Form1 tự navigate theo DANG_NHAP; href giữ cho preview ngoài WebView2.
    window.location.href = target + '?u=' + encodeURIComponent(user.hoTen ?? user.tenDangNhap ?? '');
  } catch (err) {
    alertDialog(err.message, 'Đăng nhập không thành công', 'err');
    document.getElementById('loginError').textContent = err.message;
  }
}

document.addEventListener('DOMContentLoaded', () => {
  document.getElementById('loginForm')?.addEventListener('submit', e => { e.preventDefault(); doLogin(); });
  document.getElementById('formRegister')?.addEventListener('submit', e => { e.preventDefault(); doRegister(); });
  document.getElementById('btnSwitchRegister')?.addEventListener('click', () =>
    switchAuthTab(document.getElementById('formRegister').style.display === 'none' ? 'register' : 'login'));
  document.querySelectorAll('.demo-role-link').forEach(btn => {
    btn.addEventListener('click', () => {
      document.getElementById('loginUsername').value = btn.dataset.demoUser;
      document.getElementById('loginPassword').value = '';
      document.getElementById('loginPassword').focus();
    });
  });
});
