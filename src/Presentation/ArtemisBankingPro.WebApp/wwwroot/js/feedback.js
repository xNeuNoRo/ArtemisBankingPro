const DEFAULT_DURATION = 5000;
const VARIANTS = new Set(["success", "error", "warning", "info"]);

/** @returns {HTMLElement} */
function getContainer() {
  let container = document.getElementById("feedback-container");
  if (container) return container;

  container = document.createElement("div");
  container.id = "feedback-container";
  container.className = "feedback-container";
  container.setAttribute("aria-live", "polite");
  container.setAttribute("aria-relevant", "additions text");
  document.body.append(container);
  return container;
}

/**
 * @param {string} message
 * @param {string} [variant]
 * @param {{ duration?: number }} [options]
 */
export function showFeedback(message, variant = "info", options = {}) {
  if (!message.trim()) return;

  const safeVariant = VARIANTS.has(variant) ? variant : "info";
  const item = document.createElement("div");
  item.className = `feedback feedback-${safeVariant}`;
  item.setAttribute("role", safeVariant === "error" ? "alert" : "status");

  const text = document.createElement("p");
  text.className = "feedback-message";
  text.textContent = message;
  item.append(text);

  const close = document.createElement("button");
  close.type = "button";
  close.className = "feedback-close";
  close.setAttribute("aria-label", "Cerrar mensaje");
  close.textContent = "×";
  close.addEventListener("click", () => item.remove());
  item.append(close);

  getContainer().append(item);
  const duration = options.duration ?? DEFAULT_DURATION;
  if (duration > 0) window.setTimeout(() => item.remove(), duration);
}

export function initializeFeedback() {
  document.querySelectorAll("[data-feedback-message]").forEach((element) => {
    const messageElement = /** @type {HTMLElement} */ (element);
    showFeedback(
      messageElement.dataset.feedbackMessage ?? messageElement.textContent ?? "",
      messageElement.dataset.feedbackVariant ?? "info",
    );
    messageElement.remove();
  });
}

document.addEventListener("DOMContentLoaded", () => initializeFeedback());
