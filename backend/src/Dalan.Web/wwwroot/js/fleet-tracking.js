(function () {
    "use strict";
    const root = document.querySelector("[data-fleet-tracking]");
    if (!root) return;
    const status = root.querySelector("[data-fleet-status]");
    const rows = root.querySelector("[data-fleet-rows]");
    let map = null, markers = [], firstFit = true, loading = false;
    let basemapFailed = false;
    const preference = matchMedia("(prefers-color-scheme: dark)");
    function style() {
        const theme = document.documentElement.dataset.theme;
        return (theme === "dark" || (theme === "system" && preference.matches)) && root.dataset.darkStyleUrl
            ? root.dataset.darkStyleUrl : root.dataset.styleUrl;
    }
    try {
        map = new maplibregl.Map({ container: root.querySelector("[data-fleet-map]"), style: style(),
            center: [121.03, 14.6], zoom: 11, attributionControl: true });
        map.addControl(new maplibregl.NavigationControl());
        map.on("error", () => { basemapFailed = true; status.textContent = "Basemap unavailable. Vehicle positions and timestamps remain available below."; });
        const updateTheme = () => { basemapFailed = false; map.setStyle(style()); };
        new MutationObserver(updateTheme).observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
        preference.addEventListener("change", updateTheme);
    } catch { status.textContent = "Map unavailable. Current positions are listed below."; }
    async function refresh() {
        if (loading || document.hidden) return;
        loading = true;
        try {
            const response = await fetch(root.dataset.feedUrl, { cache: "no-store", redirect: "error" });
            if (!response.ok) throw new Error();
            const data = await response.json();
            markers.forEach(marker => marker.remove()); markers = [];
            rows.replaceChildren();
            if (!data.enabled) { status.textContent = "Tracking is disabled."; return; }
            const points = [];
            for (const vehicle of data.vehicles) {
                const row = document.createElement("tr");
                const coordinate = Number.isFinite(vehicle.latitude) && Number.isFinite(vehicle.longitude) ? `${vehicle.latitude.toFixed(6)}, ${vehicle.longitude.toFixed(6)}` : "No GPS sample";
                for (const value of [vehicle.registrationNumber, vehicle.driverName || "—", vehicle.status + " · " + coordinate,
                    vehicle.capturedAtUtc ? new Date(vehicle.capturedAtUtc).toISOString() : "—", vehicle.accuracyMeters == null ? "—" : vehicle.accuracyMeters.toFixed(0) + " m"]) {
                    const cell = document.createElement("td"); cell.textContent = value; row.append(cell);
                }
                rows.append(row);
                if (map && Number.isFinite(vehicle.latitude) && Number.isFinite(vehicle.longitude)) {
                    const text = `${vehicle.registrationNumber} · ${vehicle.status}`;
                    const element = document.createElement("div"); element.className = "fleet-position" + (vehicle.status === "Fresh" ? "" : " fleet-position-stale");
                    element.textContent = vehicle.registrationNumber; element.setAttribute("aria-label", text);
                    markers.push(new maplibregl.Marker({ element }).setLngLat([vehicle.longitude, vehicle.latitude])
                        .setPopup(new maplibregl.Popup().setText(text + " · " + new Date(vehicle.capturedAtUtc).toISOString())).addTo(map));
                    points.push([vehicle.longitude, vehicle.latitude]);
                }
            }
            if (firstFit && points.length && map) { const bounds = new maplibregl.LngLatBounds(); points.forEach(point => bounds.extend(point)); map.fitBounds(bounds, { padding: 70, maxZoom: 15, duration: 0 }); firstFit = false; }
            status.textContent = `${basemapFailed || !map ? "Basemap unavailable. " : ""}Positions refreshed ${new Date(data.generatedAtUtc).toISOString()}. ${data.vehicles.filter(v => v.status === "Fresh").length} fresh GPS fixes.`;
        } catch {
            markers.forEach(marker => marker.remove()); markers = []; rows.replaceChildren();
            status.textContent = "Current positions unavailable. Previous locations are hidden until refresh succeeds.";
        } finally { loading = false; }
    }
    refresh(); const timer = setInterval(refresh, 15000);
    document.addEventListener("visibilitychange", () => { if (!document.hidden) refresh(); });
    window.addEventListener("pagehide", () => clearInterval(timer), { once: true });
})();
