// Shell Quản lý — dữ liệu thật M1+M2 (khu/phòng/người thuê, scope theo khu được gán).
// Không có số tiền: KPI đếm phòng/người, server chặn mọi action doanh thu ở RBAC.
document.addEventListener('DOMContentLoaded', async () => {
  const params = new URLSearchParams(window.location.search);
  const hoTen = params.get('u');
  if (hoTen) document.querySelector('.user-name').textContent = hoTen;

  const set = (id, v) => { const el = document.getElementById(id); if (el) el.textContent = v; };

  async function loadOverview() {
    try {
      const khus = await window.bridge.call('KHU_TRO_LAY_TAT_CA', {}) || [];
      set('mgKpiKhu', Array.isArray(khus) ? khus.length : 0);

      const phongs = await window.bridge.call('PHONG_LAY_TAT_CA', {}) || [];
      const list = Array.isArray(phongs) ? phongs : (phongs.items || []);
      const dangThue = list.filter(p => p.trang_thai === 'DaThue' || p.trang_thai === 'DaThue').length;
      const trong = list.filter(p => p.trang_thai === 'Trong').length;
      set('mgKpiPhong', list.length);
      set('mgKpiDangThue', dangThue);
      set('mgKpiTrong', trong);

      const grid = document.getElementById('managerRoomGrid');
      if (grid) {
        grid.innerHTML = list.slice(0, 24).map(p => `
          <div class="room-tile ${String(p.trang_thai).toLowerCase()}">
            <div class="room-no">${esc(p.so_phong)}</div>
            <span class="pill ${String(p.trang_thai).toLowerCase()}">${p.trang_thai}</span>
          </div>`).join('');
      }

      // người thuê khu tôi — điền bảng cũ của pane khach_thue
      const kh = await window.bridge.call('KHACH_THUE_TIM_KIEM', { TuKhoa: '' }) || {};
      const khs = Array.isArray(kh) ? kh : (kh.items || []);
      const body = document.getElementById('khach_thue-body');
      if (body) {
        body.innerHTML = khs.slice(0, 50).map(k => `<tr>
          <td>${esc(k.ho_ten ?? '')}</td><td>${esc(k.cccd ?? '')}</td>
          <td>${esc(k.so_dien_thoai ?? '')}</td><td>${esc(k.so_phong ?? '—')}</td>
          <td>${esc(k.trang_thai ?? '')}</td></tr>`).join('');
      }
    } catch (err) { toast(err.message, 'err'); }
  }

  // Nav: Tổng quan (s03) / Phòng (s04) / Người thuê (s05) — tab cũ điều hướng bằng main.js chutro không có ở đây
  document.querySelectorAll('.nav-btn[data-goto]').forEach(btn => {
    btn.addEventListener('click', () => {
      const id = btn.dataset.goto;
      document.querySelectorAll('.screen-view').forEach(s => s.classList.toggle('active', s.id === id));
      document.querySelectorAll('.nav-btn[data-goto]').forEach(b => b.classList.toggle('active', b.dataset.goto === id));
      if (id === 's03') loadOverview();
      if (id === 's04') {
        const tp = document.getElementById('tab-phong');
        if (tp) { tp.hidden = false; if (typeof loadRooms === 'function') loadRooms(); }
      }
      if (id === 's05') {
        const tk = document.getElementById('tab-khach_thue');
        if (tk) { tk.hidden = false; if (typeof loadTenants === 'function') loadTenants(); }
      }
    });
  });

  loadOverview();
  startAutoRefresh(loadOverview, 30000);
});
