(function () {
    "use strict";
    function warehouse(value) {
        if (!value || value.latitude === null || value.longitude === null ||
            String(value.latitude ?? "").trim() === "" || String(value.longitude ?? "").trim() === "") return null;
        const latitude = Number(value.latitude), longitude = Number(value.longitude);
        if (!Number.isFinite(latitude) || Math.abs(latitude) > 90 || !Number.isFinite(longitude) || Math.abs(longitude) > 180) return null;
        const address = String(value.address || "Warehouse").trim();
        if (!address || address.length > 500) return null;
        return { latitude, longitude, address };
    }
    const keyFor = owner => owner ? "dalan-warehouse:" + owner : null;
    if (typeof module !== "undefined") module.exports = { warehouse, keyFor };
    if (typeof document === "undefined") return;
    if (window.__dalanWarehouseMounted) return;
    window.__dalanWarehouseMounted = true;
    const savedDefault = fetch('/Warehouse/Current', { headers: { Accept: 'application/json' }, signal: AbortSignal.timeout(5000) })
        .then(response => response.ok ? response.json() : null).catch(() => null);
    savedDefault.then(saved => {
        document.querySelectorAll('[data-warehouse-global]').forEach(element => {
            element.textContent = saved ? `Default warehouse: ${saved.name}` : 'Set your default warehouse.';
        });
    });

    document.querySelectorAll("[data-warehouse-selection]").forEach(form => {
        const latitude = document.getElementById(form.dataset.warehouseLatitude);
        const longitude = document.getElementById(form.dataset.warehouseLongitude);
        const address = document.getElementById(form.dataset.warehouseAddress);
        const status = form.querySelector("[data-warehouse-status]");
        const key = keyFor(form.dataset.warehouseOwner);
        if (!latitude || !longitude || !address || !key) return;
        const buttons = [...form.elements].filter(element => element.tagName === 'BUTTON' && element.type === 'submit').map(button=>[button,button.disabled]);
        buttons.forEach(([button])=>button.disabled=true);
        savedDefault.finally(()=>buttons.forEach(([button,disabled])=>button.disabled=disabled));
        const current = () => warehouse({ latitude: latitude.value, longitude: longitude.value, address: address.value });
        let edited = false, applying = false;
        [latitude,longitude,address].forEach(input=>input.addEventListener('input',()=>{ if(!applying) edited=true; }));
        savedDefault.then(saved => {
            if (!saved || edited || form.dataset.warehouseRestore !== 'true') return;
            applying = true;
            latitude.value = String(saved.latitude); longitude.value = String(saved.longitude); address.value = saved.name;
            form.dataset.warehouseSaved = 'true';
            [latitude,longitude,address].forEach(input=>input.readOnly=true);
            delete address.dataset.googleAddress;
            [latitude,longitude,address].forEach(input=>input.dispatchEvent(new Event('input',{bubbles:true})));
            applying = false;
            document.dispatchEvent(new CustomEvent('warehouse-default-loaded',{detail:{form}}));
            if(status) status.textContent = 'Using your saved default warehouse. Change it using the warehouse selector for future deliveries and plans.';
        });
        try {
            if (form.dataset.warehouseRestore === "true" && !current()) {
                const saved = warehouse(JSON.parse(localStorage.getItem(key) || "null"));
                if (saved) {
                    latitude.value = String(saved.latitude);
                    longitude.value = String(saved.longitude);
                    address.value = saved.address;
                    if (status) status.textContent = "Using your warehouse selection saved in this browser.";
                }
            }
        } catch { /* Manual selection remains available when storage is unavailable. */ }
        function save() {
            try {
                const selected = current();
                if (selected) localStorage.setItem(key, JSON.stringify({ ...selected,
                    address: address.dataset.googleAddress === "true" ? "Warehouse" : selected.address }));
                else localStorage.removeItem(key);
                if (status) status.textContent = selected ? address.dataset.googleAddress === "true" ? "Warehouse map pin remembered. Confirm the address again for each transaction." : "Warehouse remembered for your account in this browser." : "Select the warehouse on the map or enter its coordinates.";
            } catch {
                if (status) status.textContent = "This browser could not remember the warehouse. The current form can still be used.";
            }
        }
        [latitude, longitude, address].forEach(input => {
            input.addEventListener("input", save);
            input.addEventListener("change", save);
        });
        form.addEventListener("submit", save);
    });
})();
