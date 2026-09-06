const STORAGE_KEY = "artemis-theme";
const DARK = "dark";
const LIGHT = "light";

/** @param {() => void} update */
function runViewTransition(update) {
  const prefersReducedMotion = window.matchMedia(
    "(prefers-reduced-motion: reduce)",
  ).matches;
  const transitionDocument = /** @type {Document & {
    startViewTransition?: (callback: () => void) => unknown;
  }} */ (document);

  if (prefersReducedMotion || !transitionDocument.startViewTransition) {
    update();
    return;
  }

  transitionDocument.startViewTransition(update);
}

/** @returns {"dark" | "light" | null} */
function readStoredTheme() {
  try {
    const stored = window.localStorage.getItem(STORAGE_KEY);
    return stored === DARK || stored === LIGHT ? stored : null;
  } catch {
    return null;
  }
}

/** @returns {"dark" | "light"} */
function systemTheme() {
  return window.matchMedia("(prefers-color-scheme: dark)").matches
    ? DARK
    : LIGHT;
}

/** @returns {"dark" | "light"} */
function resolvedTheme() {
  return readStoredTheme() ?? systemTheme();
}

/** @param {"dark" | "light"} theme */
function applyTheme(theme) {
  const root = document.documentElement;
  root.classList.toggle(DARK, theme === DARK);
  root.dataset.theme = theme;
}

function updateThemeControls() {
  const isDark = document.documentElement.dataset.theme === DARK;
  document.querySelectorAll("[data-theme-toggle]").forEach((element) => {
    const button = /** @type {HTMLButtonElement} */ (element);
    button.setAttribute("aria-pressed", String(isDark));
    button.dataset.themeState = isDark ? DARK : LIGHT;
    const label = button.querySelector("[data-theme-label]");
    if (label) {
      label.textContent = isDark ? "Usar tema claro" : "Usar tema oscuro";
    }
  });
}

export function initializeTheme() {
  applyTheme(resolvedTheme());
}

export function initializeThemeControls() {
  document.querySelectorAll("[data-theme-toggle]").forEach((element) => {
    const button = /** @type {HTMLButtonElement} */ (element);
    if (button.dataset.bound === "true") return;
    button.dataset.bound = "true";
    button.addEventListener("click", () => {
      const nextTheme =
        document.documentElement.dataset.theme === DARK ? LIGHT : DARK;
      runViewTransition(() => {
        try {
          window.localStorage.setItem(STORAGE_KEY, nextTheme);
        } catch {
          // Theme persistence is optional; the current page still changes.
        }
        applyTheme(nextTheme);
        updateThemeControls();
      });
    });
  });

  updateThemeControls();
}

initializeTheme();
document.addEventListener("DOMContentLoaded", () => initializeThemeControls());
