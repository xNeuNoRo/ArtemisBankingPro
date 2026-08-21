const DIALOG_SELECTOR =
  'dialog.ui-dialog[data-dialog-purpose="non-financial"]';
const OPEN_TRIGGER_SELECTOR = "[data-dialog-open]";
const CLOSE_TRIGGER_SELECTOR = "[data-dialog-close]";

/** @param {HTMLElement} trigger @param {boolean} expanded */
function setTriggerState(trigger, expanded) {
  trigger.setAttribute("aria-expanded", String(expanded));
}

/** @param {HTMLDialogElement} dialog @param {ParentNode} root */
function initializeDialog(dialog, root) {
  if (dialog.dataset.bound === "true" || !dialog.id) return;

  const dialogId = dialog.id;
  const triggers = [...root.querySelectorAll(OPEN_TRIGGER_SELECTOR)].filter(
    (element) => element.getAttribute("data-dialog-open") === dialogId,
  );

  if (triggers.length === 0) return;
  if (typeof dialog.showModal !== "function") return;

  dialog.dataset.bound = "true";
  let returnFocus = /** @type {HTMLElement | null} */ (null);

  const restoreFocus = () => {
    const target = returnFocus;
    returnFocus = null;
    if (target?.isConnected && !target.hasAttribute("disabled")) {
      target.focus();
    }
  };

  const closeDialog = (returnValue = "") => {
    if (dialog.open) dialog.close(returnValue);
  };

  /** @param {MouseEvent} event */
  const openDialog = (event) => {
    event.preventDefault();
    const trigger = event.currentTarget;
    if (!(trigger instanceof HTMLElement) || dialog.open) return;

    dialog.showModal();
    returnFocus = trigger;
    trigger.setAttribute("aria-controls", dialogId);
    trigger.setAttribute("aria-haspopup", "dialog");
    setTriggerState(trigger, true);
  };

  triggers.forEach((element) => {
    const trigger = /** @type {HTMLElement} */ (element);
    trigger.setAttribute("aria-controls", dialogId);
    trigger.setAttribute("aria-haspopup", "dialog");
    setTriggerState(trigger, false);
    trigger.addEventListener("click", openDialog);
  });

  dialog.querySelectorAll(CLOSE_TRIGGER_SELECTOR).forEach((element) => {
    const closeTrigger = /** @type {HTMLElement} */ (element);
    closeTrigger.addEventListener("click", () => {
      closeDialog(closeTrigger.dataset.dialogResult ?? "");
    });
  });

  dialog.addEventListener("cancel", () => {
    // Native Escape handling dispatches cancel before the dialog closes.
    dialog.returnValue = "";
  });

  dialog.addEventListener("close", () => {
    triggers.forEach((element) =>
      setTriggerState(/** @type {HTMLElement} */ (element), false),
    );
    restoreFocus();
  });

  if (dialog.dataset.dialogCloseOnBackdrop === "true") {
    dialog.addEventListener("click", (event) => {
      if (event.target === dialog) closeDialog("backdrop");
    });
  }
}

/** @param {ParentNode} root */
export function initializeDialogs(root = document) {
  root.querySelectorAll(DIALOG_SELECTOR).forEach((element) => {
    initializeDialog(/** @type {HTMLDialogElement} */ (element), root);
  });
}

document.addEventListener("DOMContentLoaded", () => initializeDialogs());
