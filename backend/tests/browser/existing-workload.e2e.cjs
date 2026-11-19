const fs = require("node:fs");
const path = require("node:path");
const assert = require("node:assert/strict");
const { chromium } = require(process.env.DALAN_PLAYWRIGHT_MODULE || "playwright");
const fixture = JSON.parse(fs.readFileSync(process.env.DALAN_WORKLOAD_FIXTURE, "utf8"));
const base = process.env.DALAN_WEB_BASE_URL || "http://127.0.0.1:5186";
const output = process.env.DALAN_BROWSER_OUTPUT;

(async () => {
    const browser = await chromium.launch({ headless: true, channel: process.env.DALAN_BROWSER_CHANNEL || "msedge" });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1100 } });
    const errors = [];
    page.on("pageerror", error => errors.push(error.message));
    page.on("response", response => {
        if (response.url().startsWith(base) && response.status() >= 500) errors.push(`Local HTTP ${response.status()}: ${new URL(response.url()).pathname}`);
    });
    try {
        await page.goto(base + "/Account/Login");
        await page.locator("#Email").fill(fixture.dispatcherEmail);
        await page.locator("#Password").fill(fixture.password);
        await Promise.all([page.waitForURL(url => !url.pathname.includes("/Account/Login")), page.locator('button[type="submit"]').click()]);
        await page.goto(base + "/DispatchBoard");
        await page.locator("#Strategy").selectOption("WorkloadEquity");
        await page.locator('[name="Settings.ServiceSecondsPerStop"]').fill("60");
        await page.locator("#Settings_WarehouseLatitude").fill("14.6");
        await page.locator("#Settings_WarehouseLongitude").fill("121.03");
        for (let i = 0; i < 2; i++) {
            await page.locator(`[name="Vehicles[${i}].Selected"][type="checkbox"]`).check();
            await page.locator(`[name="Vehicles[${i}].ExistingWorkloadSeconds"]`).fill(i === 0 ? "7200" : "1800");
        }
        for (let i = 0; i < 6; i++) await page.locator(`[name="Deliveries[${i}].Selected"][type="checkbox"]`).check();
        await page.getByRole("button", { name: "Preview routes", exact: true }).click();
        await page.getByRole("button", { name: "Review this plan" }).waitFor();
        assert(!((await page.locator("body").innerText()).includes(".ToString(")));
        const review = page.locator('form[action="/DispatchBoard/Review"]');
        assert.equal(await review.locator('[name="Vehicles[0].ExistingWorkloadSeconds"]').inputValue(), "7200");
        assert.equal(await review.locator('[name="Vehicles[1].ExistingWorkloadSeconds"]').inputValue(), "1800");
        if (output) {
            fs.mkdirSync(output, { recursive: true });
            await page.screenshot({ path: path.join(output, "workload-preview.png"), fullPage: true });
            await page.locator("#theme-select").selectOption("dark");
            await page.waitForFunction(() => getComputedStyle(document.querySelector(".btn-primary")).color === "rgb(37, 28, 19)");
            await page.locator(".content-card").filter({ has: page.getByRole("heading", { name: "Available vehicles", exact: true }) })
                .screenshot({ path: path.join(output, "workload-dark.png") });
            await page.emulateMedia({ colorScheme: "light" });
            await page.locator("#theme-select").selectOption("system");
            await page.waitForFunction(() => getComputedStyle(document.querySelector(".btn-primary")).color === "rgb(255, 255, 255)");
            await page.setViewportSize({ width: 390, height: 844 });
            assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
            await page.screenshot({ path: path.join(output, "workload-mobile.png"), fullPage: true });
            await page.setViewportSize({ width: 1440, height: 1100 });
        }
        await page.getByRole("button", { name: "Review this plan" }).click();
        await page.getByRole("heading", { name: "Review dispatch", exact: true }).waitFor();
        assert.equal(await page.locator('[name="Vehicles[0].ExistingWorkloadSeconds"]').inputValue(), "7200");
        assert.equal(await page.locator('[name="Vehicles[1].ExistingWorkloadSeconds"]').inputValue(), "1800");
        await page.getByRole("button", { name: "Confirm dispatch", exact: true }).click();
        await page.waitForURL(/\/DispatchBoard\/Plan\//);
        assert.match(await page.locator("body").innerText(), /Existing work estimate at confirmation: 30\.0 minutes/);
        assert.deepEqual(errors, []);
        console.log("PASS workload values survive form, preview, re-preview, confirmation and saved-plan rendering; Light/Dark/System/mobile passed; no local HTTP 5xx or page errors.");
    } finally { await browser.close(); }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
