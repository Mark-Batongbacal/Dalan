(function () {
    "use strict";

    function coordinate(value, max) {
        if (value === null || value === undefined || String(value).trim() === "") return null;
        const number = Number(value);
        return Number.isFinite(number) && Math.abs(number) <= max ? number : null;
    }

    function decodePolyline6(encoded) {
        if (typeof encoded !== "string" || encoded.length > 2000000) throw new Error("Invalid road geometry.");
        let index = 0, latitude = 0, longitude = 0;
        const coordinates = [];
        function delta() {
            let result = 0, shift = 0, byte;
            do {
                if (index >= encoded.length || shift > 30) throw new Error("Invalid road geometry.");
                byte = encoded.charCodeAt(index++) - 63;
                if (byte < 0 || byte > 63) throw new Error("Invalid road geometry.");
                result |= (byte & 31) << shift;
                shift += 5;
            } while (byte >= 32);
            return result & 1 ? ~(result >>> 1) : result >>> 1;
        }
        while (index < encoded.length) {
            latitude += delta(); longitude += delta();
            const lat = latitude / 1e6, lon = longitude / 1e6;
            if (coordinate(lat, 90) === null || coordinate(lon, 180) === null)
                throw new Error("Invalid road geometry.");
            coordinates.push([lon, lat]);
        }
        if (coordinates.length < 2) throw new Error("Invalid road geometry.");
        return coordinates;
    }

    if (typeof module !== "undefined" && module.exports) module.exports = { coordinate, decodePolyline6 };
    if (typeof document === "undefined") return;

    document.querySelectorAll("[data-planning-map]").forEach(root => {
        const config = JSON.parse(root.querySelector("[data-map-model]").textContent);
        const model = config.model;
        const find = selector => root.querySelector(selector);
        const byId = id => id ? document.getElementById(id) : null;
        const status = find("[data-map-status]");
        const routeSelect = find("[data-map-route]");
        const roadButton = find("[data-map-roads]");
        const targetSelect = find("[data-map-target]");
        const confirmButton = find("[data-map-confirm]");
        let pendingLocation = null;
        let candidateMarker = null;
        const confirmedLabels = new Map();
        find("[data-map-advanced]")?.addEventListener("click", () => {
            clearSelection();
            const input = byId(model.targets[Number(targetSelect.value)].latitudeInputId);
            const details = input?.closest("details");
            if (details) details.open = true;
            input?.focus();
        });
        document.addEventListener("planning-map-job-added", event => {
            const index = event.detail.index;
            for (const [kind, label] of [["Pickup", "pickup"], ["Dropoff", "drop-off"]]) {
                const target = { label: `Job ${index + 1} ${label}`, latitudeInputId: `Jobs_${index}__${kind}Latitude`,
                    longitudeInputId: `Jobs_${index}__${kind}Longitude` };
                const option = document.createElement("option");
                option.value = model.targets.length; option.textContent = target.label;
                targetSelect.append(option); model.targets.push(target);
                model.overview.push({ ...target, order: 0 });
            }
        });
        function clearSelection() {
            pendingLocation = null;
            candidateMarker?.remove(); candidateMarker = null;
            if (confirmButton) confirmButton.disabled = true;
            const selection = find("[data-map-selection]");
            if (selection) selection.textContent = "Choose a location to confirm. Existing confirmed locations are unchanged.";
        }
        function proposeLocation(lat, lon, label) {
            if (coordinate(lat, 90) === null || coordinate(lon, 180) === null) return;
            pendingLocation = { lat, lon, label: label || "Selected map point" };
            candidateMarker?.remove();
            if (map) candidateMarker = new maplibregl.Marker({ color: getComputedStyle(root).getPropertyValue("--primary").trim() })
                .setLngLat([lon, lat]).addTo(map);
            find("[data-map-selection]").textContent = model.targets[Number(targetSelect.value)].label + ": " +
                pendingLocation.label + ". Confirm to use this location.";
            confirmButton.disabled = false;
        }
        confirmButton?.addEventListener("click", () => {
            if (!pendingLocation) return;
            const { lat, lon, label } = pendingLocation;
            setLocation(lat, lon, label);
            confirmedLabels.set(targetSelect.value, label); paint();
            clearSelection();
        });
        root.closest("form")?.addEventListener("submit", event => {
            if (!pendingLocation) return;
            event.preventDefault();
            message("Confirm the selected location before continuing, or choose another location to keep existing inputs.");
            confirmButton.focus();
        });
        root.closest("form")?.addEventListener("invalid", event => {
            const details = event.target.closest("details");
            if (details) details.open = true;
        }, true);
        let map = null, markers = [], stale = false, mapFailed = false, styleReady = false;
        let request = null, requestVersion = 0, geometry = [];
        let searchRequest = null, searchVersion = 0;
        const message = text => { status.textContent = text; };
        const emptyFeatures = () => ({ type: "FeatureCollection", features: [] });

        function resolve(point) {
            if (point.selectionInputId && !byId(point.selectionInputId)?.checked) return null;
            const lat = coordinate(point.latitudeInputId ? byId(point.latitudeInputId)?.value : point.latitude, 90);
            const lon = coordinate(point.longitudeInputId ? byId(point.longitudeInputId)?.value : point.longitude, 180);
            const order = point.orderInputId ? Number(byId(point.orderInputId)?.value) : point.order;
            return lat === null || lon === null || !Number.isFinite(order) ? null : { ...point, lat, lon, order };
        }

        function pointsFor(route) {
            return route.points
                .filter(point => !point.vehicleInputId || byId(point.vehicleInputId)?.value === route.id)
                .map(resolve).filter(Boolean).sort((a, b) => a.order - b.order);
        }
        function activeRoute() { return model.routes.find(route => route.id === routeSelect?.value); }
        function visiblePoints() {
            const selected = !stale && activeRoute();
            return selected ? pointsFor(selected) :
                [...model.overview.map(resolve).filter(Boolean), ...(!stale ? model.routes.flatMap(pointsFor) : [])];
        }

        function renderGeometry() {
            if (!map || !styleReady) return;
            if (!map.getSource("dalan-road")) {
                map.addSource("dalan-road", { type: "geojson", data: emptyFeatures() });
                map.addLayer({ id: "dalan-road-outline", type: "line", source: "dalan-road",
                    paint: { "line-color": getComputedStyle(root).getPropertyValue("--card").trim(), "line-width": 8 } });
                map.addLayer({ id: "dalan-road-line", type: "line", source: "dalan-road",
                    layout: { "line-cap": "round", "line-join": "round" },
                    paint: { "line-color": getComputedStyle(root).getPropertyValue("--primary").trim(), "line-width": 4 } });
            }
            map.getSource("dalan-road").setData({ type: "FeatureCollection", features: geometry });
        }

        function clearRoad() {
            request?.abort(); request = null; requestVersion++; geometry = [];
            renderGeometry();
        }

        function paint() {
            markers.forEach(marker => marker.remove()); markers = [];
            const points = visiblePoints();
            const list = find("[data-map-location-list]");
            if (list) list.replaceChildren(...model.targets.map((target, index) => {
                const item = document.createElement("li");
                const valid = coordinate(byId(target.latitudeInputId)?.value, 90) !== null &&
                    coordinate(byId(target.longitudeInputId)?.value, 180) !== null;
                item.textContent = target.label + ": " + (valid ?
                    byId(target.addressInputId)?.value || confirmedLabels.get(String(index)) || "Location selected" : "Not selected");
                return item;
            }));
            if (roadButton) roadButton.disabled = !!request || stale || !activeRoute() || pointsFor(activeRoute()).length < 2;
            if (!map) return;
            points.forEach((point, index) => {
                const button = document.createElement("button");
                button.type = "button"; button.className = "planning-map-marker";
                button.textContent = point.order === -1 ? "S" : activeRoute() && !stale ? String(point.order + 1) : "•";
                button.setAttribute("aria-label", point.label); button.title = point.label;
                const popupContent = document.createElement("div"); popupContent.textContent = point.label;
                markers.push(new maplibregl.Marker({ element: button })
                    .setLngLat([point.lon, point.lat])
                    .setPopup(new maplibregl.Popup({ offset: 20 }).setDOMContent(popupContent)).addTo(map));
            });
        }

        function fit() {
            const points = visiblePoints();
            if (!map || points.length === 0) return;
            const bounds = new maplibregl.LngLatBounds();
            points.forEach(p => bounds.extend([p.lon, p.lat]));
            map.fitBounds(bounds, { padding: 55, maxZoom: 15, duration: 0 });
        }

        function changed(event) {
            if (root.contains(event.target)) return;
            const id = event.target.id || "";
            model.targets.forEach((target, index) => {
                if ([target.latitudeInputId, target.longitudeInputId].includes(id)) confirmedLabels.delete(String(index));
            });
            const watched = model.targets.some(t => [t.latitudeInputId, t.longitudeInputId, t.selectionInputId].includes(id)) ||
                model.overview.some(p => p.selectionInputId === id) ||
                model.routes.some(r => r.points.some(p => [p.latitudeInputId, p.longitudeInputId, p.orderInputId, p.vehicleInputId].includes(id)));
            const previewChange = model.preview && /^(Vehicles_|Deliveries_|Settings_|Strategy$)/.test(id);
            if (!watched && !previewChange) return;
            clearRoad();
            if (previewChange) { stale = true; if (routeSelect) routeSelect.disabled = true; }
            paint();
            message(stale ? "Inputs changed. Preview again to display the updated routes." :
                "Location or stop order changed. Load the road route again when ready.");
        }
        document.addEventListener("input", changed);
        document.addEventListener("change", changed);

        function setLocation(lat, lon, label) {
            const target = model.targets[Number(targetSelect?.value)];
            if (!target) return;
            for (const [id, value] of [[target.latitudeInputId, lat], [target.longitudeInputId, lon]]) {
                const input = byId(id);
                if (input) { input.value = value.toFixed(6); input.dispatchEvent(new Event("input", { bubbles: true })); }
            }
            if (label && target.addressInputId) {
                const address = byId(target.addressInputId);
                if (address) { address.value = label; address.dispatchEvent(new Event("input", { bubbles: true })); }
            }
            if (target.selectionInputId && byId(target.selectionInputId)) {
                byId(target.selectionInputId).checked = true;
                byId(target.selectionInputId).dispatchEvent(new Event("change", { bubbles: true }));
            }
            clearRoad(); paint();
            const form = root.closest("form");
            const summary = form?.querySelector(".validation-summary-errors[data-valmsg-summary]");
            const validator = form && window.jQuery?.(form).data("validator");
            if (summary && validator) {
                validator.form();
                const list = summary.querySelector("ul");
                list?.replaceChildren(...validator.errorList.map(error => {
                    const item = document.createElement("li"); item.textContent = error.message; return item;
                }));
                summary.classList.toggle("validation-summary-errors", validator.errorList.length > 0);
                summary.classList.toggle("validation-summary-valid", validator.errorList.length === 0);
            }
            message(target.label + " set to " + lat.toFixed(6) + ", " + lon.toFixed(6) +
                ". Check the form before saving." + (stale ? " Preview routes again." : ""));
        }

        function style() {
            const theme = document.documentElement.dataset.theme;
            const dark = theme === "dark" || (theme === "system" && matchMedia("(prefers-color-scheme: dark)").matches);
            return dark && config.darkStyleUrl ? config.darkStyleUrl : config.styleUrl;
        }
        try {
            map = new maplibregl.Map({ container: find("[data-map-canvas]"), style: style(),
                center: [120.9842, 14.5995], zoom: 11, attributionControl: true });
            map.addControl(new maplibregl.NavigationControl({ showCompass: false }), "top-right");
            map.scrollZoom.disable();
            map.on("click", event => {
                if (model.targets.length && !event.originalEvent.target.closest(".planning-map-marker, .maplibregl-popup, .maplibregl-control-container"))
                    proposeLocation(event.lngLat.lat, event.lngLat.lng);
            });
            map.on("load", () => {
                mapFailed = false; renderGeometry(); paint(); fit();
                if (/^(Loading map\.|Retrying map|Map tiles are taking)/.test(status.textContent))
                    message("Map ready. Select locations or choose a route to load its road geometry.");
            });
            map.on("style.load", () => {
                styleReady = true; renderGeometry(); paint();
                if (status.textContent.startsWith("Retrying map")) message("Map style reloaded. Stop locations and coordinate controls are available.");
            });
            map.on("error", () => {
                mapFailed = true;
                message("Some map tiles could not load. Coordinate inputs and stop lists still work. Use Retry map to try again.");
            });
            setTimeout(() => {
                if (map && !map.loaded() && !mapFailed && status.textContent.startsWith("Loading map."))
                    message("Map tiles are taking longer to load. Coordinate inputs still work; use Retry map if needed.");
            }, 12000);
            const updateTheme = () => { if (map) { styleReady = false; map.setStyle(style()); } };
            new MutationObserver(updateTheme).observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
            matchMedia("(prefers-color-scheme: dark)").addEventListener("change", updateTheme);
            new ResizeObserver(() => requestAnimationFrame(() => { map.resize(); fit(); }))
                .observe(find("[data-map-canvas]"));
        } catch {
            mapFailed = true;
            message("The map could not start. Use address search or coordinate fields; existing stop lists remain available.");
        }

        find("[data-map-fit]").addEventListener("click", fit);
        find("[data-map-retry]").addEventListener("click", () => {
            if (map) { mapFailed = false; styleReady = false; map.setStyle(style()); message("Retrying map…"); }
            else message("Map graphics are unavailable in this browser. Use address search or coordinate fields.");
        });
        find("[data-map-use-center]")?.addEventListener("click", () => {
            if (!map) return message("Map graphics are unavailable. Use coordinate fields or address search.");
            const center = map.getCenter(); proposeLocation(center.lat, center.lng);
        });
        targetSelect?.addEventListener("change", () => {
            clearSelection();
            searchRequest?.abort(); searchVersion++;
            find("[data-map-search-button]").disabled = false;
            find("[data-map-results]").replaceChildren();
            find("[data-map-search]").value = "";
            const target = model.targets[Number(targetSelect.value)];
            const lat = coordinate(byId(target.latitudeInputId)?.value, 90);
            const lon = coordinate(byId(target.longitudeInputId)?.value, 180);
            if (map && lat !== null && lon !== null) map.easeTo({ center: [lon, lat], duration: 0 });
            message("Select a point for " + target.label + " or use its coordinate fields.");
        });
        routeSelect?.addEventListener("change", () => {
            clearRoad(); paint(); fit();
            message(activeRoute() ? "Stop markers show the selected order. Load road route for actual road geometry." : "Showing stop locations. Select a route to load road geometry.");
        });

        async function post(url, payload, signal) {
            const token = root.querySelector('input[name="__RequestVerificationToken"]') ||
                root.closest("form")?.querySelector('input[name="__RequestVerificationToken"]') ||
                document.querySelector('input[name="__RequestVerificationToken"]');
            if (!token) throw new Error("Reload this page before trying again.");
            const response = await fetch(url, { method: "POST", credentials: "same-origin", signal,
                headers: { "Content-Type": "application/json",
                    "RequestVerificationToken": token.value },
                body: JSON.stringify(payload) });
            const content = response.headers.get("content-type") || "";
            if (!content.includes("json")) throw new Error("Your session may have expired. Sign in again, then retry.");
            const body = await response.json();
            if (!response.ok) throw new Error(body.title || "The request failed. Try again.");
            return body;
        }

        roadButton?.addEventListener("click", async () => {
            const route = activeRoute(); if (!route || stale) return;
            const points = pointsFor(route);
            if (points.length < 2) return message("Set at least two locations first.");
            clearRoad();
            const version = requestVersion;
            request = new AbortController();
            roadButton.disabled = true; message("Loading the road route…");
            try {
                const body = await post(root.dataset.routeUrl,
                    { points: points.map(p => ({ latitude: p.lat, longitude: p.lon })) }, request.signal);
                if (version !== requestVersion) return;
                geometry = body.legs.map(leg => ({ type: "Feature", properties: {},
                    geometry: { type: "LineString", coordinates: decodePolyline6(leg.encodedPolyline6) } }));
                renderGeometry();
                const meters = body.legs.reduce((sum, leg) => sum + leg.distanceMeters, 0);
                const seconds = body.legs.reduce((sum, leg) => sum + leg.durationSeconds, 0);
                message("Road route loaded: " + (meters / 1000).toFixed(1) + " km, " + Math.round(seconds / 60) +
                    " min driving. Saved dispatch estimates are unchanged." + (mapFailed ? " Some map tiles are unavailable." : ""));
            } catch (error) {
                if (version === requestVersion && error.name !== "AbortError")
                    message(error.message + " Stop markers and lists remain available.");
            } finally {
                if (version === requestVersion) { request = null; paint(); }
            }
        });

        const searchButton = find("[data-map-search-button]");
        const searchInput = find("[data-map-search]");
        searchInput?.addEventListener("input", () => {
            clearSelection();
            searchButton.disabled = false;
            searchRequest?.abort(); searchVersion++; find("[data-map-results]").replaceChildren();
            message("Press Search to look up this address, or choose a map point.");
        });
        searchInput?.addEventListener("keydown", event => { if (event.key === "Enter") { event.preventDefault(); searchButton.click(); } });
        searchButton?.addEventListener("click", async () => {
            const query = searchInput.value.trim();
            if (query.length < 3 || query.length > 200) return message("Enter 3–200 characters to search.");
            searchRequest?.abort(); const version = ++searchVersion; searchRequest = new AbortController();
            const results = find("[data-map-results]"); results.replaceChildren();
            searchButton.disabled = true; message("Searching OpenStreetMap addresses…");
            try {
                const body = await post(root.dataset.searchUrl, { query }, searchRequest.signal);
                if (version !== searchVersion) return;
                for (const result of body.results) {
                    const row = document.createElement("li"), button = document.createElement("button");
                    button.type = "button"; button.className = "planning-map-result";
                    button.textContent = result.label;
                    button.addEventListener("click", () => {
                        proposeLocation(result.location.latitude, result.location.longitude, result.label);
                        map?.easeTo({ center: [result.location.longitude, result.location.latitude], zoom: 15, duration: 0 });
                        results.replaceChildren();
                    });
                    row.append(button); results.append(row);
                }
                message(body.results.length ? "Select an address to use it. " + body.attribution :
                    "No matching addresses. Select a map point or enter coordinates.");
            } catch (error) {
                if (version === searchVersion && error.name !== "AbortError") message(error.message);
            } finally {
                if (version === searchVersion) searchButton.disabled = false;
            }
        });
        paint();
    });
})();
