const FOCUSABLE_SELECTOR =
  "a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex='-1'])";

/** @param {Element} element @returns {element is HTMLElement} */
function isVisibleHtmlElement(element) {
  return element instanceof HTMLElement && !element.hidden;
}

/** @param {Element} container @returns {HTMLElement[]} */
function focusableElements(container) {
  return [...container.querySelectorAll(FOCUSABLE_SELECTOR)].filter(isVisibleHtmlElement);
}

/** @param {HTMLElement} drawer @param {HTMLElement | null} backdrop @param {HTMLElement | null} trigger @param {boolean} open */
function setDrawerState(drawer, backdrop, trigger, open) {
  drawer.classList.toggle("is-open", open);
  drawer.setAttribute("aria-hidden", String(!open));
  drawer.toggleAttribute("inert", !open);
  if (backdrop) backdrop.hidden = !open;
  if (trigger) trigger.setAttribute("aria-expanded", String(open));
  document.body.classList.toggle(
    "drawer-open",
    open && !window.matchMedia("(min-width: 64rem)").matches,
  );
}

export function initializeNavigation() {
  initializeDrawers();
  initializeMenus();
}

document.addEventListener("DOMContentLoaded", () => initializeNavigation());

function initializeDrawers() {
  document.querySelectorAll("[data-navigation-drawer]").forEach((element) => {
    const drawer = /** @type {HTMLElement} */ (element);
    if (drawer.dataset.bound === "true") return;

    const name = drawer.dataset.navigationDrawer;
    if (!name) return;
    drawer.dataset.bound = "true";
    const trigger = /** @type {HTMLElement | null} */ (
      document.querySelector(`[data-navigation-toggle='${name}']`)
    );
    const close = /** @type {HTMLElement | null} */ (
      document.querySelector(`[data-navigation-close='${name}']`)
    );
    const backdrop = /** @type {HTMLElement | null} */ (
      document.querySelector(`[data-navigation-backdrop='${name}']`)
    );
    const desktopQuery = window.matchMedia("(min-width: 64rem)");
    let returnFocus = trigger;

    const open = () => {
      returnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : trigger;
      setDrawerState(drawer, backdrop, trigger, true);
      focusableElements(drawer)[0]?.focus();
    };
    const closeDrawer = () => {
      drawer.dataset.userOpen = "false";
      setDrawerState(drawer, backdrop, trigger, false);
      if (returnFocus?.isConnected) returnFocus.focus();
    };

    const openDrawer = () => {
      drawer.dataset.userOpen = "true";
      open();
    };

    trigger?.addEventListener("click", openDrawer);
    close?.addEventListener("click", closeDrawer);
    backdrop?.addEventListener("click", closeDrawer);

    document.addEventListener("keydown", (event) => {
      if (desktopQuery.matches || !drawer.classList.contains("is-open")) return;
      if (event.key === "Escape") {
        event.preventDefault();
        closeDrawer();
        return;
      }
      if (event.key !== "Tab") return;
      const elements = focusableElements(drawer);
      if (elements.length === 0) return;
      const first = elements[0];
      const last = elements[elements.length - 1];
      if (!first || !last) return;
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    });

    const syncViewportState = () => {
      setDrawerState(
        drawer,
        backdrop,
        trigger,
        desktopQuery.matches || drawer.dataset.userOpen === "true",
      );
    };

    desktopQuery.addEventListener("change", syncViewportState);
    drawer.dataset.userOpen = "false";
    syncViewportState();
  });
}

function initializeMenus() {
  document.querySelectorAll("[data-user-menu]").forEach((element) => {
    const menu = /** @type {HTMLElement} */ (element);
    if (menu.dataset.bound === "true") return;
    const trigger = /** @type {HTMLButtonElement | null} */ (
      menu.querySelector("[data-user-menu-toggle]")
    );
    const panel = /** @type {HTMLElement | null} */ (
      menu.querySelector("[data-user-menu-panel]")
    );
    if (!trigger || !panel) return;

    menu.dataset.bound = "true";
    const close = () => {
      panel.dataset.userMenuState = "closed";
      panel.setAttribute("aria-hidden", "true");
      trigger.setAttribute("aria-expanded", "false");
      trigger.setAttribute("aria-label", "Abrir menú de usuario");
    };
    const toggle = () => {
      const open = panel.dataset.userMenuState !== "open";
      panel.dataset.userMenuState = open ? "open" : "closed";
      panel.setAttribute("aria-hidden", String(!open));
      trigger.setAttribute("aria-expanded", String(open));
      trigger.setAttribute(
        "aria-label",
        open ? "Cerrar menú de usuario" : "Abrir menú de usuario",
      );
      if (open) {
        const firstAction = /** @type {HTMLElement | null} */ (
          panel.querySelector("a, button")
        );
        firstAction?.focus();
      }
    };

    trigger.addEventListener("click", toggle);
    document.addEventListener("click", (event) => {
      if (!(event.target instanceof Node) || !menu.contains(event.target)) close();
    });
    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape" && panel.dataset.userMenuState === "open") {
        close();
        trigger.focus();
      }
    });
    close();
  });
}
