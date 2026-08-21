/** @param {ParentNode} root */
export function initializeUserForms(root = document) {
  root.querySelectorAll("[data-user-role]").forEach((element) => {
    const role = /** @type {HTMLSelectElement} */ (element);
    if (role.dataset.bound === "true") return;

    const form = role.closest("form");
    if (!(form instanceof HTMLFormElement)) return;

    const amountField = form.querySelector("[data-role-dependent='Cliente']");
    const amountInput = amountField?.querySelector("input");
    if (!(amountField instanceof HTMLElement) || !(amountInput instanceof HTMLInputElement)) {
      return;
    }

    role.dataset.bound = "true";
    const sync = () => {
      const isClient = role.value === "Cliente";
      amountField.hidden = !isClient;
      amountInput.disabled = !isClient;
      if (!isClient) amountInput.value = "";
    };

    role.addEventListener("change", sync);
    sync();
  });
}

document.addEventListener("DOMContentLoaded", () => initializeUserForms());
