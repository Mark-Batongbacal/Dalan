(function () {
    "use strict";
    const form = document.querySelector("[data-analytics-filter]");
    if (!form) return;
    const status = document.querySelector("[data-analytics-loading]");
    const button = form.querySelector('button[type="submit"]');
    // The report remains a normal GET navigation, including when JavaScript is unavailable.
    form.addEventListener("submit", function () {
        if (!form.checkValidity()) return;
        status.textContent = "Loading the selected period… Any figures below are from the previous request until the page reloads.";
        form.setAttribute("aria-busy", "true");
        button.disabled = true;
    });
    window.addEventListener("pageshow", function () {
        status.textContent = "";
        form.removeAttribute("aria-busy");
        button.disabled = false;
    });
})();
