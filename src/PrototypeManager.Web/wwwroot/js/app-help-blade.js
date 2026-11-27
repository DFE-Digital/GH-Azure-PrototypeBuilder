(function () {
  const toggle = document.getElementById("app-help-toggle");
  const blade = document.getElementById("app-help");
  const backdrop = document.getElementById("app-help-backdrop");
  const closeButton = document.getElementById("app-help-close");

  if (!toggle || !blade || !backdrop || !closeButton) {
    return;
  }

  let lastFocus = null;

  function isOpen() {
    return !blade.hasAttribute("hidden");
  }

  function openBlade() {
    if (isOpen()) {
      return;
    }

    lastFocus = document.activeElement;
    blade.hidden = false;
    backdrop.hidden = false;
    document.body.classList.add("app-help-open");
    toggle.setAttribute("aria-expanded", "true");
    closeButton.focus();
  }

  function closeBlade() {
    if (!isOpen()) {
      return;
    }

    blade.hidden = true;
    backdrop.hidden = true;
    document.body.classList.remove("app-help-open");
    toggle.setAttribute("aria-expanded", "false");

    if (lastFocus && typeof lastFocus.focus === "function") {
      lastFocus.focus();
    } else {
      toggle.focus();
    }
  }

  function toggleBlade() {
    if (isOpen()) {
      closeBlade();
    } else {
      openBlade();
    }
  }

  toggle.addEventListener("click", toggleBlade);
  closeButton.addEventListener("click", closeBlade);
  backdrop.addEventListener("click", closeBlade);

  document.addEventListener("keydown", function (event) {
    if (event.key === "Escape" && isOpen()) {
      event.preventDefault();
      closeBlade();
    }
  });
})();
