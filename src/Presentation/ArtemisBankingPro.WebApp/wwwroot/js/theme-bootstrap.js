(() => {
  const root = document.documentElement;
  root.dataset.js = "true";
  const dark = "dark";
  const light = "light";
  let stored = null;

  try {
    const value = window.localStorage.getItem("artemis-theme");
    stored = value === dark || value === light ? value : null;
  } catch {
    // A blocked storage API must not prevent the page from rendering.
  }

  const theme = stored ?? (
    window.matchMedia("(prefers-color-scheme: dark)").matches ? dark : light
  );
  root.classList.toggle(dark, theme === dark);
  root.dataset.theme = theme;
})();
