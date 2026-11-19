const fs = require("node:fs");
const path = require("node:path");
const os = require("node:os");
const assert = require("node:assert/strict");
const { chromium } = require(process.env.DALAN_PLAYWRIGHT_MODULE || "playwright");

function polyline(points) {
    let last = [0, 0], encoded = "";
    for (const point of points) {
        const current = [Math.round(point.latitude * 1e6), Math.round(point.longitude * 1e6)];
        current.forEach((value, i) => {
            let delta = value - last[i]; last[i] = value;
            delta = delta < 0 ? ~(delta << 1) : delta << 1;
            while (delta >= 32) { encoded += String.fromCharCode((32 | (delta & 31)) + 63); delta >>>= 5; }
            encoded += String.fromCharCode(delta + 63);
        });
    }
    return encoded;
}
(async () => {
    const fixturePath = process.env.DALAN_MAP_TEST_FIXTURE;
    if (!fixturePath) throw new Error("Set DALAN_MAP_TEST_FIXTURE to a disposable fixture manifest.");
    const fixture = JSON.parse(fs.readFileSync(fixturePath, "utf8").replace(/^\uFEFF/, ""));
    const base = process.env.DALAN_MAP_TEST_BASE_URL || "http://127.0.0.1:5184";
    const output = process.env.DALAN_MAP_TEST_OUTPUT || path.join(os.tmpdir(), "dalan-map-browser");
    fs.mkdirSync(output, { recursive: true });
    const live = process.env.DALAN_MAP_TEST_LIVE === "true";
    const browser = await chromium.launch({ headless: true, channel: process.env.DALAN_BROWSER_CHANNEL,
        args: ["--enable-unsafe-swiftshader"] });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1050 } });
    const errors = [];
    page.on("pageerror", error => errors.push(error.message));
    // Keep a test-only reference to the real MapLibre instance to inspect drawn geometry.
    await page.route("**/js/planning-map.js*", async route => {
        const response = await route.fetch();
        await route.fulfill({ response, body:
            "if(window.maplibregl){maplibregl.Map=new Proxy(maplibregl.Map,{construct(t,a){const m=Reflect.construct(t,a);window.__dln14Map=m;return m;}});}\n" +
            await response.text() });
    });
    if (!live) await page.route("**/PlanningMap/Route", async route => {
        const points = route.request().postDataJSON().points;
        await route.fulfill({ json: { legs: [{ encodedPolyline6: polyline(points), distanceMeters: 1200, durationSeconds: 240 }] } });
    });
    async function login() {
        await page.goto(base + "/Account/Login");
        await page.locator("#Email").fill(fixture.dispatcherEmail);
        await page.locator("#Password").fill(fixture.password);
        await Promise.all([page.waitForURL(url => !url.pathname.includes("/Account/Login")),
            page.locator('button[type="submit"]').click()]);
    }
    async function settle(prefix) {
        await page.waitForFunction(text => !document.querySelector("[data-map-status]").textContent.startsWith(text),
            prefix, { timeout: 45000 });
    }
    async function draw() {
        await page.locator("[data-map-roads]").click();
        await settle("Loading the road");
        assert.match(await page.locator("[data-map-status]").innerText(), /^Road route loaded:/);
        await page.waitForFunction(() => window.__dln14Map?.getStyle()?.sources?.["dalan-road"]?.data?.features?.length > 0,
            null, { timeout: 25000 });
    }
    try {
        await login();
        await page.goto(base + "/Deliveries/Create");
        await page.waitForSelector(".maplibregl-canvas");
        await page.locator("[data-map-use-center]").focus();
        await page.keyboard.press("Enter");
        assert.equal(await page.locator("#PickupLatitude").inputValue(), "14.599500");
        await page.locator("#map-target").selectOption("1");
        await page.locator("[data-map-use-center]").click();
        await page.locator("#DropoffLatitude").fill("14.6095");
        await page.locator("#DropoffLongitude").fill("120.9942");
        await page.locator("#map-route").selectOption("delivery");
        await draw();
        console.log("PASS: manual/keyboard-center coordinates and road geometry (" + (live ? "live Valhalla" : "fixture") + ")");
        await page.locator("[data-planning-map]").screenshot({ path: path.join(output, "delivery-light.png") });

        await page.locator("#theme-select").selectOption("dark");
        await page.waitForFunction(() => getComputedStyle(document.querySelector("[data-planning-map]")).backgroundColor === "rgb(35, 38, 43)");
        await page.waitForTimeout(3000);
        await page.locator("[data-planning-map]").screenshot({ path: path.join(output, "delivery-dark.png") });
        await page.emulateMedia({ colorScheme: "light" });
        await page.locator("#theme-select").selectOption("system");
        await page.waitForFunction(() => getComputedStyle(document.querySelector("[data-planning-map]")).backgroundColor === "rgb(255, 255, 255)");
        await page.setViewportSize({ width: 390, height: 844 });
        await page.waitForTimeout(2500);
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 2));
        await page.locator("[data-planning-map]").screenshot({ path: path.join(output, "delivery-mobile.png") });
        await page.setViewportSize({ width: 1440, height: 1050 });
        console.log("PASS: Light/Dark/System styling and narrow viewport");

        await page.locator("#DropoffLatitude").fill("14.6100");
        assert.match(await page.locator("[data-map-status]").innerText(), /changed/);
        assert.equal(await page.evaluate(() => window.__dln14Map.getStyle()?.sources?.["dalan-road"]?.data?.features?.length || 0), 0);
        console.log("PASS: editing coordinates clears stale geometry");

        // Deterministic empty, unsafe-label, provider failure and search-selection states.
        await page.route("**/PlanningMap/Search", route => route.fulfill({ json: { results: [], attribution: "OpenStreetMap" } }));
        await page.locator("[data-map-search]").fill("No such address");
        await page.locator("[data-map-search]").press("Enter"); await settle("Searching");
        assert.match(await page.locator("[data-map-status]").innerText(), /No matching addresses/);
        await page.unroute("**/PlanningMap/Search");
        await page.route("**/PlanningMap/Search", route => route.fulfill({ json: {
            results: [{ label: "<img src=x onerror=alert(1)>", location: { latitude: 14.61, longitude: 120.99 } }],
            attribution: "OpenStreetMap · Photon" } }));
        await page.locator("[data-map-search]").fill("Test address");
        await page.locator("[data-map-search-button]").click(); await settle("Searching");
        assert.equal(await page.locator("[data-map-results] img").count(), 0);
        await page.locator(".planning-map-result").click();
        assert.equal(await page.locator("#DropoffLatitude").inputValue(), "14.610000");
        await page.unroute("**/PlanningMap/Search");
        await page.route("**/PlanningMap/Search", route => route.fulfill({ status: 503,
            contentType: "application/problem+json", body: JSON.stringify({ title: "Address search is unavailable." }) }));
        await page.locator("[data-map-search-button]").click(); await settle("Searching");
        assert.match(await page.locator("[data-map-status]").innerText(), /unavailable/);
        await page.unroute("**/PlanningMap/Search");
        console.log("PASS: empty search, safe text labels, selected coordinates, provider failure");

        if (live) {
            await page.locator("[data-map-search]").fill("Manila City Hall");
            await page.locator("[data-map-search-button]").click(); await settle("Searching");
            assert.ok(await page.locator(".planning-map-result").count() > 0, await page.locator("[data-map-status]").innerText());
            console.log("PASS: live Photon address results");
        }
        await page.locator("#RecipientName").fill("DLN14 disposable browser delivery");
        await page.locator("#PickupAddress").fill("Temporary manual pickup");
        await page.locator("#DropoffAddress").fill("Temporary manual drop-off");
        await Promise.all([page.waitForURL(url => url.pathname.includes("/Deliveries/Details")),
            page.getByRole("button", { name: "Create delivery", exact: true }).click()]);
        console.log("PASS: existing delivery creation saves map-selected form coordinates");

        await page.goto(base + "/DispatchBoard");
        const vehicle = page.locator('input[type=checkbox][name$=".Selected"][name^="Vehicles"]').first();
        const delivery = page.locator('input[type=checkbox][name$=".Selected"][name^="Deliveries"]').first();
        await vehicle.check(); await delivery.check();
        await page.locator("#Settings_WarehouseLatitude").fill("14.6");
        await page.locator("#Settings_WarehouseLongitude").fill("121.03");
        await page.getByRole("button", { name: "Preview routes", exact: true }).click();
        await page.getByRole("heading", { name: "Proposed routes" }).waitFor({ timeout: 45000 });
        await page.locator("[data-map-route]").selectOption({ index: 1 });
        await draw();
        await page.locator("[data-planning-map]").screenshot({ path: path.join(output, "dispatch-preview.png") });
        await page.locator("#Settings_WarehouseLatitude").fill("14.6001");
        assert.match(await page.locator("[data-map-status]").innerText(), /Preview again/);
        assert.ok(await page.locator("[data-map-roads]").isDisabled());
        await page.locator("#Settings_WarehouseLatitude").fill("14.6");
        await page.getByRole("button", { name: "Preview routes", exact: true }).click();
        await page.getByRole("heading", { name: "Proposed routes" }).waitFor({ timeout: 45000 });
        console.log("PASS: changed preview inputs disable stale routes until a fresh preview");
        await page.getByRole("button", { name: "Review this plan", exact: true }).click();
        await page.waitForURL(url => url.pathname.includes("/DispatchBoard/Review"));
        await page.locator("[data-map-route]").selectOption({ index: 1 });
        await draw();
        assert.equal(await page.locator(".planning-map-marker").count(), 3);
        const originalOrder = await page.locator("#Stops_1__Order").inputValue();
        await page.locator("#Stops_1__Order").fill("2");
        assert.equal(await page.evaluate(() => window.__dln14Map.getStyle()?.sources?.["dalan-road"]?.data?.features?.length || 0), 0);
        await page.locator("#Stops_1__Order").fill(originalOrder);
        console.log("PASS: review edits clear road geometry without confirming or optimizing");
        await page.getByRole("button", { name: "Confirm dispatch", exact: true }).click();
        await page.waitForURL(url => url.pathname.includes("/DispatchBoard/Plan"), { timeout: 45000 });
        await page.locator("[data-map-route]").selectOption({ index: 1 });
        await draw();
        await page.locator("[data-planning-map]").screenshot({ path: path.join(output, "confirmed-plan.png") });
        console.log("PASS: actual dispatch preview -> editable review -> SQL confirmation -> saved map");

        await page.route("https://tiles.openfreemap.org/**", route => route.abort());
        await page.goto(base + "/Deliveries/Create");
        await page.waitForFunction(() => document.querySelector("[data-map-status]").textContent.includes("could not load"));
        await page.locator("#PickupLatitude").fill("0");
        assert.equal(await page.locator("#PickupLatitude").inputValue(), "0");
        assert.ok(await page.locator("[data-map-retry]").isVisible());
        await page.unroute("https://tiles.openfreemap.org/**");
        await page.locator("[data-map-retry]").click();
        await page.waitForFunction(() => document.querySelector("[data-map-status]").textContent.includes("reloaded"), null, { timeout: 30000 });
        console.log("PASS: tile failure retains coordinate input and Retry");
        assert.deepEqual(errors, []);
        console.log("PASS: no uncaught browser errors");
        console.log("Screenshots: " + output);
    } catch (error) {
        await page.screenshot({ path: path.join(output, "failure.png"), fullPage: true });
        console.error(JSON.stringify({ status: await page.locator("[data-map-status]").innerText(), errors,
            map: await page.evaluate(() => ({ present: !!window.__dln14Map,
                loaded: window.__dln14Map?.isStyleLoaded(), sources: Object.keys(window.__dln14Map?.getStyle()?.sources || {}),
                features: window.__dln14Map?.getStyle()?.sources?.["dalan-road"]?.data?.features?.length })) }));
        throw error;
    } finally { await browser.close(); }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
