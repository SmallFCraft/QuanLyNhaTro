// Shell Quản lý — dữ liệu thật (khu/phòng/sự cố/đề xuất, scope theo khu được gán).
// Không có số tiền: KPI đếm phòng/khu, server chặn mọi action doanh thu ở RBAC.
document.addEventListener('DOMContentLoaded', async () => {
  const params = new URLSearchParams(window.location.search);
  const hoTen = params.get('u');
  if (hoTen) {
    const uEl = document.querySelector('.user-name');
    if (uEl) uEl.textContent = hoTen;
  }

  let cachedRooms = [];
  const set = (id, v) => { const el = document.getElementById(id); if (el) el.textContent = v; };

  const LOAI_DX_LABEL = {
    ChamDutHopDong: 'Chấm dứt HĐ',
    SuaGia: 'Sửa giá',
    MuaSam: 'Mua sắm',
    SuaChuaLon: 'Sửa chữa lớn',
    Khac: 'Khác',
  };

  const MUC_DO_LABEL = {
    KhanCap: 'Khẩn cấp',
    Cao: 'Cao',
    TrungBinh: 'Trung bình',
    Thap: 'Thấp',
  };

  const TRANG_THAI_SC_LABEL = {
    Moi: 'Mới',
    DangXuLy: 'Đang xử lý',
    DaXuLy: 'Đã xử lý',
  };

  const TRANG_THAI_DX_LABEL = {
    ChoDuyet: 'Chờ duyệt',
    DaDuyet: 'Đã duyệt',
    TuChoi: 'Từ chối',
  };

  function mucDoPill(mucDo) {
    const map = {
      KhanCap: 'pill-red',
      Cao: 'pill-amber',
      TrungBinh: 'pill-gold',
      Thap: 'pill-green',
    };
    return `<span class="pill ${map[mucDo] || 'pill-gray'}">${esc(MUC_DO_LABEL[mucDo] || mucDo)}</span>`;
  }

  function suCoStatusPill(trangThai) {
    const map = {
      Moi: 'pill-red',
      DangXuLy: 'pill-amber',
      DaXuLy: 'pill-green',
    };
    return `<span class="pill ${map[trangThai] || 'pill-gray'}">${esc(TRANG_THAI_SC_LABEL[trangThai] || trangThai)}</span>`;
  }

  function deXuatPill(trangThai) {
    const map = {
      ChoDuyet: 'pill-amber',
      DaDuyet: 'pill-green',
      TuChoi: 'pill-red',
    };
    return `<span class="pill ${map[trangThai] || 'pill-gray'}">${esc(TRANG_THAI_DX_LABEL[trangThai] || trangThai)}</span>`;
  }

  function roomStatusPill(trangThai) {
    const map = {
      Trong: ['pill-green', 'Trống'],
      DaThue: ['pill-amber', 'Đang thuê'],
      BaoTri: ['pill-red', 'Bảo trì'],
    };
    const [cls, label] = map[trangThai] || ['pill-gray', trangThai || '—'];
    return `<span class="pill ${cls}">${esc(label)}</span>`;
  }

  // ── loadOverview ──────────────────────────────────────────────────────────
  async function loadOverview() {
    // 1. Khu trọ
    try {
      const khus = await window.bridge.call('KHU_TRO_LAY_TAT_CA', {}) || [];
      const khuList = Array.isArray(khus) ? khus : (khus.items || []);
      set('mgKpiKhu', khuList.length);
      if (khuList.length === 1 && (khuList[0].tenKhuTro || khuList[0].ten_khu_tro)) {
        const areaName = khuList[0].tenKhuTro ?? khuList[0].ten_khu_tro;
        const chip = document.querySelector('.area-chip');
        if (chip) chip.textContent = areaName;
      }
    } catch (e) {
      toast(e.message, 'err');
    }

    // 2. Phòng khu tôi
    try {
      const phongs = await window.bridge.call('PHONG_LAY_TAT_CA', {}) || [];
      const list = Array.isArray(phongs) ? phongs : (phongs.items || []);
      cachedRooms = list;
      const getStatus = p => p.trangThai ?? p.trang_thai ?? '';
      const getSoPhong = p => p.soPhong ?? p.so_phong ?? '';
      const dangThue = list.filter(p => getStatus(p) === 'DaThue').length;
      const trong = list.filter(p => getStatus(p) === 'Trong').length;
      set('mgKpiPhong', list.length);
      set('mgKpiDangThue', dangThue);
      set('mgKpiTrong', trong);

      const grid = document.getElementById('managerRoomGrid');
      if (grid) {
        if (list.length === 0) {
          grid.innerHTML = '<p class="helper-text">Chưa có phòng nào trong khu được gán.</p>';
        } else {
          grid.innerHTML = list.slice(0, 24).map(p => {
            const st = getStatus(p);
            return `
              <div class="room-tile ${esc(String(st).toLowerCase())}">
                <div class="room-no">${esc(getSoPhong(p))}</div>
                ${roomStatusPill(st)}
              </div>`;
          }).join('');
        }
      }
    } catch (e) {
      toast(e.message, 'err');
    }

    // 3. Sự cố khu tôi (sắp xếp KhanCap trước từ server)
    try {
      const suCos = await window.bridge.call('SU_CO_LAY_THEO_KHU', {}) || [];
      const suCoList = Array.isArray(suCos) ? suCos : (suCos.items || []);
      const incidentEl = document.getElementById('managerIncidentsList');
      if (incidentEl) {
        if (suCoList.length === 0) {
          incidentEl.innerHTML = '<p class="helper-text">Không có sự cố nào trong khu.</p>';
        } else {
          incidentEl.innerHTML = suCoList.map(sc => {
            const id = sc.id;
            const mucDo = sc.mucDo ?? sc.muc_do ?? 'TrungBinh';
            const trangThai = sc.trangThai ?? sc.trang_thai ?? 'Moi';
            const soPhong = sc.soPhong ?? sc.so_phong ?? sc.phongId ?? '';
            const moTa = sc.moTa ?? sc.mo_ta ?? '';
            const ketQua = sc.ketQua ?? sc.ket_qua ?? '';
            const actionBtn = trangThai !== 'DaXuLy'
              ? `<button type="button" class="btn btn-secondary btn-sm" data-update-sc="${Number(id)}" data-sc-status="${esc(trangThai)}">Cập nhật</button>`
              : '';
            return `
              <div class="incident-row" style="display:flex;align-items:center;justify-content:space-between;gap:10px;padding:10px 0;border-bottom:1px solid rgba(15,23,42,0.06);flex-wrap:wrap;">
                <div style="display:flex;flex-direction:column;gap:4px;min-width:0;flex:1;">
                  <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;">
                    ${mucDoPill(mucDo)}
                    <strong>Phòng ${esc(soPhong)}</strong>
                    ${suCoStatusPill(trangThai)}
                  </div>
                  <div style="font-size:13px;color:var(--text);">${esc(moTa)}</div>
                  ${ketQua ? `<div class="helper-text">Kết quả: ${esc(ketQua)}</div>` : ''}
                </div>
                <div>${actionBtn}</div>
              </div>`;
          }).join('');

          incidentEl.querySelectorAll('[data-update-sc]').forEach(btn => {
            btn.addEventListener('click', () => {
              const scId = Number(btn.dataset.updateSc);
              const curStatus = btn.dataset.scStatus;
              openUpdateIncidentModal(scId, curStatus);
            });
          });
        }
      }
    } catch (e) {
      const incidentEl = document.getElementById('managerIncidentsList');
      if (incidentEl) incidentEl.innerHTML = '<p class="helper-text">Không tải được danh sách sự cố.</p>';
    }

    // 4. Đề xuất của tôi / khu tôi
    try {
      const deXuats = await window.bridge.call('DE_XUAT_LAY_TAT_CA', {}) || [];
      const dxList = Array.isArray(deXuats) ? deXuats : (deXuats.items || []);
      const tbody = document.getElementById('managerProposalsBody')
        || document.querySelector('#managerProposalsCard tbody');
      if (tbody) {
        if (dxList.length === 0) {
          tbody.innerHTML = '<tr><td colspan="4" style="color:var(--text-muted);">Chưa có đề xuất nào.</td></tr>';
        } else {
          tbody.innerHTML = dxList.map(dx => {
            const loai = dx.loai ?? '';
            const nd = dx.noiDung ?? dx.noi_dung ?? '';
            const shortNd = nd.length > 50 ? nd.substring(0, 50) + '…' : nd;
            const tt = dx.trangThai ?? dx.trang_thai ?? '';
            const ph = dx.phanHoi ?? dx.phan_hoi ?? '';
            return `<tr>
              <td>${esc(LOAI_DX_LABEL[loai] || loai)}</td>
              <td title="${esc(nd)}">${esc(shortNd)}</td>
              <td>${deXuatPill(tt)}</td>
              <td>${ph ? esc(ph) : '<span style="color:var(--text-muted);">—</span>'}</td>
            </tr>`;
          }).join('');
        }
      }
    } catch (e) {
      const tbody = document.getElementById('managerProposalsBody')
        || document.querySelector('#managerProposalsCard tbody');
      if (tbody) tbody.innerHTML = '<tr><td colspan="4" style="color:var(--text-muted);">Không tải được đề xuất.</td></tr>';
    }
  }

  // ── Modal: Báo sự cố (SU_CO_TAO) ──────────────────────────────────────────
  function openReportIncidentModal() {
    const roomField = cachedRooms.length > 0
      ? `<select id="sc-phongId" class="select">
          ${cachedRooms.map(p => `<option value="${Number(p.id)}">Phòng ${esc(p.soPhong ?? p.so_phong ?? p.id)}</option>`).join('')}
        </select>`
      : `<input id="sc-phongId" type="number" class="input" placeholder="Nhập ID phòng" min="1" />`;

    const html = `
      <div class="field">
        <label for="sc-phongId">Phòng</label>
        ${roomField}
      </div>
      <div class="field">
        <label for="sc-mucDo">Mức độ</label>
        <select id="sc-mucDo" class="select">
          <option value="Thap">Thấp</option>
          <option value="TrungBinh" selected>Trung bình</option>
          <option value="Cao">Cao</option>
          <option value="KhanCap">Khẩn cấp</option>
        </select>
      </div>
      <div class="field">
        <label for="sc-moTa">Mô tả sự cố</label>
        <textarea id="sc-moTa" class="textarea" rows="4" maxlength="1000" placeholder="Mô tả chi tiết sự cố..."></textarea>
      </div>
      <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:12px;">
        <button type="button" class="btn btn-secondary btn-sm" id="sc-cancel-btn">Hủy</button>
        <button type="button" class="btn btn-primary btn-sm" id="sc-submit-btn">Gửi báo cáo</button>
      </div>`;

    UIChrome.openModal('Báo sự cố phòng', html);
    document.getElementById('sc-cancel-btn')?.addEventListener('click', () => UIChrome.closeModal());
    const submitBtn = document.getElementById('sc-submit-btn');
    submitBtn?.addEventListener('click', async () => {
      const phongId = parseInt(document.getElementById('sc-phongId')?.value || '', 10);
      const mucDo = document.getElementById('sc-mucDo')?.value || 'TrungBinh';
      const moTa = (document.getElementById('sc-moTa')?.value || '').trim();
      if (!Number.isFinite(phongId) || phongId <= 0 || !moTa) {
        UIChrome.showToast('Vui lòng chọn phòng và nhập mô tả sự cố.');
        return;
      }
      try {
        submitBtn.disabled = true;
        submitBtn.textContent = 'Đang gửi…';
        await window.bridge.call('SU_CO_TAO', { phongId, mucDo, moTa });
        UIChrome.closeModal();
        UIChrome.showToast('Đã báo sự cố thành công.');
        await loadOverview();
      } catch (e) {
        UIChrome.showToast('Lỗi: ' + e.message);
        submitBtn.disabled = false;
        submitBtn.textContent = 'Gửi báo cáo';
      }
    });
  }

  // ── Modal: Cập nhật trạng thái sự cố (SU_CO_CAP_NHAT_TRANG_THAI) ──────────
  function openUpdateIncidentModal(suCoId, currentStatus) {
    const html = `
      <div class="field">
        <label for="scu-trangThai">Trạng thái mới</label>
        <select id="scu-trangThai" class="select">
          <option value="DangXuLy" ${currentStatus === 'Moi' ? 'selected' : ''}>Đang xử lý</option>
          <option value="DaXuLy" ${currentStatus === 'DangXuLy' ? 'selected' : ''}>Đã xử lý</option>
        </select>
      </div>
      <div class="field">
        <label for="scu-ketQua">Kết quả xử lý (tùy chọn)</label>
        <textarea id="scu-ketQua" class="textarea" rows="3" maxlength="1000" placeholder="Ghi chú phương án / kết quả khắc phục..."></textarea>
      </div>
      <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:12px;">
        <button type="button" class="btn btn-secondary btn-sm" id="scu-cancel-btn">Hủy</button>
        <button type="button" class="btn btn-primary btn-sm" id="scu-submit-btn">Lưu cập nhật</button>
      </div>`;

    UIChrome.openModal(`Cập nhật sự cố #${suCoId}`, html);
    document.getElementById('scu-cancel-btn')?.addEventListener('click', () => UIChrome.closeModal());
    const submitBtn = document.getElementById('scu-submit-btn');
    submitBtn?.addEventListener('click', async () => {
      const trangThai = document.getElementById('scu-trangThai')?.value || 'DangXuLy';
      const ketQua = (document.getElementById('scu-ketQua')?.value || '').trim();
      try {
        submitBtn.disabled = true;
        submitBtn.textContent = 'Đang lưu…';
        await window.bridge.call('SU_CO_CAP_NHAT_TRANG_THAI', {
          suCoId,
          trangThai,
          ketQua: ketQua || undefined,
        });
        UIChrome.closeModal();
        UIChrome.showToast('Đã cập nhật trạng thái sự cố.');
        await loadOverview();
      } catch (e) {
        UIChrome.showToast('Lỗi: ' + e.message);
        submitBtn.disabled = false;
        submitBtn.textContent = 'Lưu cập nhật';
      }
    });
  }

  // ── Modal: Gửi đề xuất (DE_XUAT_TAO) ──────────────────────────────────────
  function openNewProposalModal() {
    const html = `
      <div class="field">
        <label for="dx-loai">Loại đề xuất</label>
        <select id="dx-loai" class="select">
          <option value="MuaSam">Mua sắm</option>
          <option value="SuaChuaLon">Sửa chữa lớn</option>
          <option value="SuaGia">Sửa giá phòng</option>
          <option value="ChamDutHopDong">Chấm dứt hợp đồng</option>
          <option value="Khac">Khác</option>
        </select>
      </div>
      <div class="field">
        <label for="dx-noiDung">Nội dung đề xuất</label>
        <textarea id="dx-noiDung" class="textarea" rows="4" maxlength="1000" placeholder="Trình bày lý do và chi tiết đề xuất gửi Chủ trọ..."></textarea>
      </div>
      <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:12px;">
        <button type="button" class="btn btn-secondary btn-sm" id="dx-cancel-btn">Hủy</button>
        <button type="button" class="btn btn-primary btn-sm" id="dx-submit-btn">Gửi đề xuất</button>
      </div>`;

    UIChrome.openModal('Gửi đề xuất lên Chủ trọ', html);
    document.getElementById('dx-cancel-btn')?.addEventListener('click', () => UIChrome.closeModal());
    const submitBtn = document.getElementById('dx-submit-btn');
    submitBtn?.addEventListener('click', async () => {
      const loai = document.getElementById('dx-loai')?.value || 'Khac';
      const noiDung = (document.getElementById('dx-noiDung')?.value || '').trim();
      if (!noiDung) {
        UIChrome.showToast('Vui lòng nhập nội dung đề xuất.');
        return;
      }
      try {
        submitBtn.disabled = true;
        submitBtn.textContent = 'Đang gửi…';
        await window.bridge.call('DE_XUAT_TAO', { loai, noiDung });
        UIChrome.closeModal();
        UIChrome.showToast('Đã gửi đề xuất chờ Chủ trọ duyệt.');
        await loadOverview();
      } catch (e) {
        UIChrome.showToast('Lỗi: ' + e.message);
        submitBtn.disabled = false;
        submitBtn.textContent = 'Gửi đề xuất';
      }
    });
  }

  document.getElementById('btnManagerReportIssue')?.addEventListener('click', openReportIncidentModal);
  document.getElementById('btnManagerNewProposal')?.addEventListener('click', openNewProposalModal);

  // ── Nav: Tổng quan (s03) / Phòng (s04) / Người thuê (s05) ────────────────
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
