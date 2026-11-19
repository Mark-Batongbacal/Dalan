(function () {
    "use strict";
    function nextSlot(now) {
        const result = new Date(now);
        result.setMinutes(Math.floor(result.getMinutes() / 30) * 30 + 30, 0, 0);
        return result;
    }
    function localValue(date) {
        const pad = value => String(value).padStart(2, "0");
        return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
    }
    if (typeof module !== "undefined") module.exports = { nextSlot, localValue };
    if (typeof document === "undefined") return;
    const input = document.querySelector("[data-delivery-time]");
    const utc = document.getElementById("ExpectedDeliveryAtUtc");
    if (!input || !utc) return;
    // jQuery's numeric step rule does not support datetime-local. Use this
    // control's native half-hour validity, retaining the existing numeric rule.
    if (window.jQuery?.validator) {
        const validator = window.jQuery.validator;
        const numericStep = validator.methods.step;
        validator.addMethod("step", function (value, element, parameter) {
            return element === input ? this.optional(element) || !element.validity.stepMismatch
                : numericStep.call(this, value, element, parameter);
        }, validator.messages.step);
    }
    const now = new Date();
    input.min = localValue(nextSlot(now));
    if (!input.value && input.dataset.timeDefault === "true") {
        input.value = localValue(nextSlot(now));
    }
    const zone = document.querySelector("[data-time-zone]");
    if (zone) zone.textContent = `Times shown in ${Intl.DateTimeFormat().resolvedOptions().timeZone}. Defaults to the next half-hour from now; adjust in 30-minute steps.`;
    function sync() {
        input.setCustomValidity("");
        if (!input.value) { utc.value = ""; return; }
        const selected = new Date(input.value);
        if (!Number.isFinite(selected.getTime()) || localValue(selected) !== input.value) {
            input.setCustomValidity("Choose a valid local date and time.");
            utc.value = "";
        } else if (selected.getTime() <= Date.now()) {
            input.setCustomValidity("Expected delivery time must be later than now.");
            utc.value = selected.toISOString();
        } else utc.value = selected.toISOString();
    }
    input.addEventListener("input", sync);
    input.addEventListener("change", sync);
    input.form.addEventListener("submit", event => {
        sync();
        if (!input.checkValidity()) { event.preventDefault(); input.reportValidity(); }
    });
    document.querySelectorAll("[data-time-step]").forEach(button => button.addEventListener("click", () => {
        const selected = input.value ? new Date(input.value) : nextSlot(new Date());
        selected.setMinutes(selected.getMinutes() + Number(button.dataset.timeStep));
        const earliest = nextSlot(new Date());
        input.value = localValue(selected < earliest ? earliest : selected);
        sync();
    }));
    sync();
})();
