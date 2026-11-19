const fs = require("node:fs");
const path = require("node:path");
const assert = require("node:assert/strict");
const { chromium } = require(process.env.DALAN_PLAYWRIGHT_MODULE || "playwright");
const fixture = JSON.parse(fs.readFileSync(process.env.DALAN_FLEET_FIXTURE, "utf8"));
const base = process.env.DALAN_WEB_BASE_URL || "http://127.0.0.1:5188";
const output = process.env.DALAN_BROWSER_OUTPUT;

(async () => {
    const browser = await chromium.launch({ headless: true, channel: "msedge" });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1100 } });
    const page = await context.newPage();
    const errors = [];
    page.on("pageerror", error => errors.push(error.message));
    page.on("response", response => {
        if (response.url().startsWith(base) && /\.(css|js|svg)(\?|$)/.test(response.url()) && response.status() >= 400)
            errors.push("Asset HTTP " + response.status() + ": " + new URL(response.url()).pathname);
    });
    try {
        await page.goto(base + "/Account/Login");
        await page.locator("#Email").fill(fixture.dispatcherEmail);
        await page.locator("#Password").fill(fixture.password);
        await page.getByRole("button", { name: /^Sign in(?: to workspace)?$/ }).click();
        await page.waitForURL(url => !url.pathname.includes("/Account/Login"));
        await page.goto(base + "/FleetTracking");
        await page.locator("[data-fleet-status]").filter({ hasText: "Positions refreshed" }).waitFor();
        assert.equal((await page.locator("[data-observed-utilization]").innerText()).trim(), "—");
        assert.match(await page.locator("[data-fleet-rows]").innerText(), /No location/);
        // Rendering/failure checks use an intercepted feed; SQL ingestion and aggregation are tested separately.
        let fail = false;
        await page.route("**/FleetTracking/Positions", route => fail
            ? route.fulfill({ status: 503, body: "Unavailable" })
            : route.fulfill({ json: { enabled: true, generatedAtUtc: new Date().toISOString(), vehicles: [
                { vehicleId: "1", registrationNumber: "FRESH-TEST", driverName: "Test driver", latitude: 14.6, longitude: 121.03,
                    accuracyMeters: 5, capturedAtUtc: new Date().toISOString(), status: "Fresh" },
                { vehicleId: "2", registrationNumber: "<img src=x onerror=alert(1)>", driverName: "Test driver", latitude: 14.601, longitude: 121.031,
                    accuracyMeters: 12, capturedAtUtc: new Date(Date.now() - 180000).toISOString(), status: "Stale" }
            ] } }));
        await page.evaluate(() => document.dispatchEvent(new Event("visibilitychange")));
        await page.locator("[data-fleet-rows]").filter({ hasText: "FRESH-TEST" }).waitFor();
        assert.equal(await page.locator("[data-fleet-rows] img").count(), 0);
        assert.equal(await page.locator(".fleet-position-stale").count(), 1);
        if (output) fs.mkdirSync(output, { recursive: true });
        for (const theme of ["light", "dark", "system"]) {
            await page.locator("#theme-select").selectOption(theme);
            await page.waitForTimeout(600);
            assert.equal(await page.locator("html").getAttribute("data-theme"), theme);
            if (output) await page.locator("#main-content").screenshot({ path: path.join(output, `fleet-${theme}.png`) });
        }
        await page.setViewportSize({ width: 390, height: 844 });
        assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
        if (output) await page.locator("#main-content").screenshot({ path: path.join(output, "fleet-mobile.png") });
        fail = true;
        await page.evaluate(() => document.dispatchEvent(new Event("visibilitychange")));
        await page.locator("[data-fleet-status]").filter({ hasText: "Previous locations are hidden" }).waitFor();
        assert.equal(await page.locator("[data-fleet-rows] tr").count(), 0);
        assert.equal(await page.locator(".fleet-position").count(), 0);
        await page.goto(base + "/Places");
        assert.match(page.url(), /\/Vehicles$/);
        assert.equal(await page.locator('.sidebar-nav a[href="/Places"]').count(),0);
        const search = await page.evaluate(async()=>{
            const token=document.querySelector('[name=__RequestVerificationToken]').value;
            const response=await fetch('/PlanningMap/Addresses',{method:'POST',headers:{'Content-Type':'application/json',RequestVerificationToken:token},body:JSON.stringify({query:'123 Main Street'})});
            return {status:response.status,body:await response.json()};
        });
        assert.equal(search.status,503); assert.match(search.body.title,/not configured/);
        await page.goto(base + "/Notifications");
        await page.locator(".alert-info").filter({ hasText: "not enabled" }).waitFor();
        assert.deepEqual(errors, []);
        console.log("Passed authenticated empty SQL feed, fresh/stale rendering, safe text, Light/Dark/System/mobile, failed-feed clearing, missing Google configuration and disabled email checks.");
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
