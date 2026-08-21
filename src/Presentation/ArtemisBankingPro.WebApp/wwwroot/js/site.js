document.addEventListener("DOMContentLoaded", () => {
  document.documentElement.dataset.js = "true";
  initAuthParticles();
  staggerCards();
});

function initAuthParticles() {
  const container = document.querySelector(".auth-particles");
  if (!container || window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;

  const colors = ["#149f9d", "#287bb7", "#74d8d0"];
  for (let index = 0; index < 18; index++) {
    const particle = document.createElement("span");
    particle.className = "auth-particle";
    const size = Math.random() * 3 + 2;
    particle.style.cssText = `width:${size}px;height:${size}px;left:${Math.random() * 100}%;bottom:-10px;--tx:${(Math.random() - 0.5) * 160}px;--duration:${Math.random() * 12 + 12}s;animation-delay:${Math.random() * -16}s;background:${colors[index % colors.length]}`;
    container.appendChild(particle);
  }
}

function staggerCards() {
  if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;

  document.querySelectorAll(".credit-card-item, .visual-card--standalone").forEach((card, index) => {
    if (!(card instanceof HTMLElement)) return;
    card.style.animation = `ui-slide-up 360ms ${index * 70}ms var(--ui-motion-ease) both`;
  });
}
