// Shell dùng chung: userbar + đăng xuất. Mỗi shell nhúng file này với biến cấu hình.
window.shell = {
  async render(elId, info) {
    const el = document.getElementById(elId);
    el.innerHTML = `
      <div class="userbar">
        <span class="ten">Quản lý nhà trọ</span>
        <span class="vai-tro">${info.vaiTro}</span>
        <span>${info.hoTen ?? ''}</span>
        ${info.phu ? `<span>${info.phu}</span>` : ''}
        <span class="ket-noi ok">Đã kết nối</span>
        <button id="btnDangXuat">Đăng xuất</button>
      </div>`;
    document.getElementById('btnDangXuat').addEventListener('click', async () => {
      await api.call('DANG_XUAT');
    });
  }
};
