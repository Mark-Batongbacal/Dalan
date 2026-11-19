const fs = require("node:fs");
const path = require("node:path");
const assert = require("node:assert/strict");
const { chromium } = require(process.env.DALAN_PLAYWRIGHT_MODULE || "playwright");
const fixture = JSON.parse(fs.readFileSync(process.env.DALAN_WAREHOUSE_FIXTURE, "utf8"));
const base = process.env.DALAN_WEB_BASE_URL || "http://127.0.0.1:5187";
const output = process.env.DALAN_BROWSER_OUTPUT;

(async () => {
    const browser = await chromium.launch({ headless: true, channel: process.env.DALAN_BROWSER_CHANNEL || "msedge" });
    const context = await browser.newContext({ timezoneId: "Asia/Manila", viewport: { width: 1440, height: 1100 } });
    const page = await context.newPage();
    const errors = [];
    page.on("pageerror", error => errors.push(error.message));
    page.on("response", response => {
        if (response.url().startsWith(base) && response.status() >= 500) errors.push("Local HTTP " + response.status());
    });
    try {
        await page.goto(base + "/Account/Login");
        await page.locator("#Email").fill(fixture.dispatcherEmail);
        await page.locator("#Password").fill(fixture.password);
        await page.getByRole("button", { name: "Sign in", exact: true }).click();
        await page.waitForURL(url => !url.pathname.includes("/Account/Login"));
        await page.goto(base + "/DispatchBoard");
        assert.equal(await page.locator('[name$=".StartLatitude"]').count(), 0);
        await page.locator(".maplibregl-canvas").waitFor();
        await page.getByRole("button", { name: "Use map center", exact: true }).click();
        await page.locator("#Settings_WarehouseAddress").fill("Browser test warehouse");
        const latitude = await page.locator("#Settings_WarehouseLatitude").inputValue();
        const longitude = await page.locator("#Settings_WarehouseLongitude").inputValue();
        assert(latitude && longitude);

        await page.goto(base + "/Deliveries/Create");
        assert.equal(await page.locator("#PickupAddress").inputValue(), "Browser test warehouse");
        assert.equal(Number(await page.locator("#PickupLatitude").inputValue()), Number(latitude));
        assert.equal(Number(await page.locator("#PickupLongitude").inputValue()), Number(longitude));
        const utc = page.locator("#ExpectedDeliveryAtUtc");
        const firstTime = Date.parse(await utc.inputValue());
        assert(firstTime > Date.now() && firstTime - Date.now() <= 30 * 60 * 1000);
        assert.match(await page.locator("#ExpectedDeliveryLocal").inputValue(), /:(00|30)$/);
        await page.locator('[data-time-step="30"]').click();
        const expectedUtc = await utc.inputValue();
        assert.equal(Date.parse(expectedUtc) - firstTime, 30 * 60 * 1000);
        await page.locator('[data-time-step="-30"]').click();
        assert.equal(Date.parse(await utc.inputValue()), firstTime);
        await page.locator('[data-time-step="30"]').click();
        assert.match(await page.locator("[data-time-zone]").innerText(), /Asia\/Manila/);
        await page.locator("#RecipientName").fill("Warehouse UI recipient");
        await page.locator("#DropoffAddress").fill("Browser test destination");
        await page.locator("#DropoffLatitude").fill("14.601");
        await page.locator("#DropoffLongitude").fill("121.031");
        await page.locator("#PayloadWeightKg").fill("2");
        assert(await page.evaluate(() => window.jQuery("#ExpectedDeliveryLocal").valid()));
        if (output) {
            fs.mkdirSync(output, { recursive: true });
            await page.locator("#main-content").screenshot({ path: path.join(output, "warehouse-delivery-light.png") });
            await page.locator("#theme-select").selectOption("dark");
            await page.waitForFunction(() => getComputedStyle(document.querySelector(".btn-primary")).color === "rgb(37, 28, 19)");
            await page.setViewportSize({ width: 390, height: 844 });
            assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
            await page.locator("#main-content").screenshot({ path: path.join(output, "warehouse-delivery-mobile-dark.png") });
            await page.locator("#theme-select").selectOption("system");
            await page.setViewportSize({ width: 1440, height: 1100 });
        }
        await page.getByRole("button", { name: "Create delivery", exact: true }).click();
        await page.waitForURL(/\/Deliveries\/Details\//);
        const deliveryId = new URL(page.url()).pathname.split("/").pop();
        await page.goto(base + "/DispatchBoard");
        assert.equal(Number(await page.locator("#Settings_WarehouseLatitude").inputValue()), Number(latitude));
        for (let i = 0; i < 2; i++) await page.locator(`[name="Vehicles[${i}].Selected"][type="checkbox"]`).check();
        await page.getByLabel(/Warehouse UI recipient/).check();
        await page.getByRole("button", { name: "Preview routes", exact: true }).click();
        await page.getByRole("heading", { name: "Selected-plan results" }).waitFor();
        await page.getByRole("button", { name: "Review this plan", exact: true }).click();
        await page.getByRole("heading", { name: "Review dispatch", exact: true }).waitFor();
        for (let i = 0; i < 2; i++) {
            assert.equal(Number(await page.locator(`[name="Vehicles[${i}].StartLatitude"]`).inputValue()), Number(latitude));
            assert.equal(Number(await page.locator(`[name="Vehicles[${i}].StartLongitude"]`).inputValue()), Number(longitude));
        }
        await page.getByRole("button", { name: "Confirm dispatch", exact: true }).click();
        await page.waitForURL(/\/DispatchBoard\/Plan\//);
        assert.deepEqual(errors, []);
        if (output) fs.writeFileSync(path.join(output, "warehouse-form-summary.json"), JSON.stringify({
            deliveryId, expectedUtc, warehouse: { address: "Browser test warehouse", latitude: Number(latitude), longitude: Number(longitude) },
            planId: new URL(page.url()).pathname.split("/").pop()
        }, null, 2));
        console.log("PASS map-selected shared warehouse, pickup persistence, local/UTC half-hour controls, common vehicle starts, confirmation, themes and mobile layout.");
    } finally { await browser.close(); }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
