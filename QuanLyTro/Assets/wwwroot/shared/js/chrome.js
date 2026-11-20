/* Runtime chrome dùng chung (từ template glassmorphism): sidebar drawer, modal, toast,
   điều hướng data-goto / data-toast / data-scroll, nút đăng xuất. */
(function (global) {
  function showToast(msg) {
    const stack = document.getElementById('toastStack');
    if (!stack) return alert(msg);
    const el = document.createElement('div');
    el.className = 'toast';
    el.textContent = msg;
    stack.appendChild(el);
    setTimeout(() => el.remove(), 3200);
  }

  function openModal(title, html) {
    document.getElementById('modalTitle').textContent = title || '';
    document.getElementById('modalBody').innerHTML = html || '';
    document.getElementById('appModal').classList.add('open');
  }

  function closeModal() { document.getElementById('appModal').classList.remove('open'); }

  function closeAllDrawers() {
    document.querySelectorAll('.sidebar.open').forEach(s => s.classList.remove('open'));
    document.getElementById('drawerBackdrop')?.classList.remove('open');
  }

  document.addEventListener('click', (e) => {
    const openDrawer = e.target.closest('[data-open-drawer]');
    if (openDrawer) {
      const sb = document.getElementById(openDrawer.dataset.openDrawer);
      if (sb) { sb.classList.add('open'); document.getElementById('drawerBackdrop').classList.add('open'); }
      return;
    }
    if (e.target.closest('[data-close-drawer]')) return closeAllDrawers();
    if (e.target.id === 'drawerBackdrop') return closeAllDrawers();
    const gotoBtn = e.target.closest('[data-goto]');
    if (gotoBtn) {
      const id = gotoBtn.dataset.goto;
      if (id === 's01') { global.QuanLyTroBridge.invoke('DANG_XUAT').catch(() => location.reload()); return; }
      document.querySelectorAll('.screen-view').forEach(s => s.classList.toggle('active', s.id === id));
      document.querySelectorAll('.nav-btn[data-goto]').forEach(b => b.classList.toggle('active', b.dataset.goto === id));
      closeAllDrawers();
      return;
    }
    const toastBtn = e.target.closest('[data-toast]');
    if (toastBtn) showToast(toastBtn.dataset.toast);
    const scrollBtn = e.target.closest('[data-scroll]');
    if (scrollBtn) document.getElementById(scrollBtn.dataset.scroll)?.scrollIntoView({ behavior: 'smooth' });
    if (e.target.id === 'appModal' || e.target.closest('#closeModalBtn')) closeModal();
  });

  global.UIChrome = { showToast, openModal, closeModal };
})(window);

/* Shim tương thích: JS nghiệp vụ repo cũ gọi window.bridge.call('ACTION', {...PascalCase}).
   Server mới đọc camelCase case-sensitive → dịch key đệ quy một tầng bằng bảng ánh xạ;
   action chưa có ở server (M3-M5) trả lỗi thân thiện thay vì treo. */
(function (global) {
  const KEY_MAP = {
    TenDangNhap: 'tenDangNhap', MatKhau: 'matKhau', HoTen: 'hoTen', NgaySinh: 'ngaySinh',
    Cccd: 'cccd', SoDienThoai: 'soDienThoai', QueQuan: 'queQuan', NoiLamViec: 'noiLamViec',
    KhuTroId: 'khuTroId', SoPhong: 'soPhong', GiaThue: 'giaThue', SoNguoiToiDa: 'soNguoiToiDa',
    TrangThai: 'trangThai', GhiChu: 'ghiChu', TuKhoa: 'tuKhoa', PhongId: 'phongId',
    LyDo: 'lyDo', TaiKhoanId: 'taiKhoanId', VaiTro: 'vaiTro', BienNhan: 'bienNhan'
  };
  function mapKeys(obj) {
    if (!obj || typeof obj !== 'object' || Array.isArray(obj)) return obj;
    const out = {};
    for (const [k, v] of Object.entries(obj)) out[KEY_MAP[k] ?? k] = mapKeys(v);
    return out;
  }
  global.bridge = {
    call(action, data) {
      return global.QuanLyTroBridge.invoke(action, mapKeys(data || {})).then(res => {
        if (res.success === false) throw new Error(res.message || 'Thao tác thất bại');
        return res.data;
      }).catch(err => {
        if (String(err.message).includes('Không tìm thấy hành động') || String(err.message).includes('NotFoundOrDenied'))
          throw new Error('Chức năng này thuộc sprint M3–M5, chưa kích hoạt trên server.');
        throw err;
      });
    },
    onMessage() { /* bridge.js template đã tự lắng nghe */ }
  };
})(window);
