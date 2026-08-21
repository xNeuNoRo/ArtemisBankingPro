/** @param {ParentNode} root */
export function initializeAuthForms(root = document) {
  root.querySelectorAll("[data-password-strength]").forEach((element) => {
    const meter = /** @type {HTMLElement} */ (element);
    if (meter.dataset.bound === "true") return;
    const inputId = meter.dataset.passwordStrength;
    const input = inputId ? document.getElementById(inputId) : null;
    const bar = /** @type {HTMLElement | null} */ (
      meter.querySelector("[data-strength-bar]")
    );
    const label = /** @type {HTMLElement | null} */ (
      meter.querySelector("[data-strength-label]")
    );
    if (!(input instanceof HTMLInputElement) || !bar || !label) return;

    meter.dataset.bound = "true";
    const update = () => {
      const value = input.value;
      const rules = [
        value.length >= 8,
        /[a-z]/.test(value) && /[A-Z]/.test(value),
        /\d/.test(value),
        /[^A-Za-z\d]/.test(value),
      ];
      const score = rules.filter(Boolean).length;
      const labels = ["Sin evaluar", "Débil", "En progreso", "Buena", "Segura"];
      bar.setAttribute("aria-valuenow", String(score * 25));
      meter.dataset.strength = String(score);
      label.textContent = labels[score] ?? "Sin evaluar";
      meter.querySelectorAll("[data-password-rule]").forEach((ruleElement) => {
        const rule = /** @type {HTMLElement} */ (ruleElement);
        const ruleName = rule.dataset.passwordRule;
        const passed = ruleName === "length"
          ? rules[0]
          : ruleName === "case"
            ? rules[1]
            : ruleName === "number"
              ? rules[2]
              : ruleName === "symbol"
                ? rules[3]
                : false;
        rule.classList.toggle("is-valid", passed);
      });
    };

    input.addEventListener("input", update);
    update();
  });
}

document.addEventListener("DOMContentLoaded", () => initializeAuthForms());
