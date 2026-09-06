/** @param {ParentNode} root */
export function initializePasswordToggles(root = document) {
  root.querySelectorAll("[data-password-toggle]").forEach((element) => {
    const button = /** @type {HTMLButtonElement} */ (element);
    if (button.dataset.bound === "true") return;
    const targetId = button.dataset.passwordToggle;
    if (!targetId) return;

    const input = document.getElementById(targetId);
    if (!(input instanceof HTMLInputElement)) return;

    button.dataset.bound = "true";
    button.setAttribute("aria-pressed", "false");
  button.setAttribute("aria-label", "Ver");
    button.addEventListener("click", () => {
      const showing = input.type === "password";
      input.type = showing ? "text" : "password";
      button.setAttribute("aria-pressed", String(showing));
      button.setAttribute(
        "aria-label",
        showing ? "Ocultar" : "Ver",
      );
      const label = button.querySelector("[data-password-label]");
      if (label) label.textContent = showing ? "Ocultar" : "Ver";
    });
  });
}

/** @param {HTMLFormElement} form */
function lockForm(form) {
  if (form.dataset.submitting === "true") return false;

  form.dataset.submitting = "true";
  form.setAttribute("aria-busy", "true");
  form
    .querySelectorAll("button[type=submit], input[type=submit]")
    .forEach((element) => {
      const submit = /** @type {HTMLButtonElement | HTMLInputElement} */ (element);
      submit.disabled = true;
      submit.setAttribute("aria-disabled", "true");
      submit.setAttribute("aria-busy", "true");
      if (submit instanceof HTMLButtonElement) {
        const label = submit.querySelector("[data-submit-label]");
        if (label) {
          label.textContent = submit.dataset.submittingLabel ?? "Procesando…";
        }
      }
    });
  return true;
}

/** @param {ParentNode} root */
export function initializeSubmitLocks(root = document) {
  root.querySelectorAll("form[data-submit-lock]").forEach((element) => {
    const form = /** @type {HTMLFormElement} */ (element);
    if (form.dataset.bound === "true") return;
    form.dataset.bound = "true";
    form.addEventListener("submit", (event) => {
      if (!form.checkValidity() || !lockForm(form)) {
        event.preventDefault();
      }
    });
  });
}

/** @param {ParentNode} root */
export function focusFirstInvalidControl(root = document) {
  const control = root.querySelector(
    '[data-autofocus-error] [aria-invalid="true"], [data-autofocus-error] .input-validation-error',
  );
  if (control instanceof HTMLElement) {
    control.focus();
  }
}

/** @param {ParentNode} root */
export function initializeForms(root = document) {
  initializePasswordToggles(root);
  initializeSubmitLocks(root);
  focusFirstInvalidControl(root);
}

document.addEventListener("DOMContentLoaded", () => initializeForms());
