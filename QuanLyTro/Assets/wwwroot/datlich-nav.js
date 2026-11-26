(() => {
  const header = document.querySelector('.booking-top');
  let overHero = window.scrollY < 48;

  function updateHeader() {
    const next = window.scrollY < (overHero ? 64 : 28);
    if (next === overHero) return;
    overHero = next;
    header.classList.add('has-scrolled');
    header.classList.toggle('is-over-hero', overHero);
  }

  window.addEventListener('scroll', updateHeader, { passive: true });
  header.classList.toggle('is-over-hero', overHero);
})();
