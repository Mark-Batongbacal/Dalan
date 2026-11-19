const fs = require("node:fs");
const path = require("node:path");
const os = require("node:os");
const assert = require("node:assert/strict");
const { chromium } = require(process.env.DALAN_PLAYWRIGHT_MODULE || "playwright");

(async () => {
    const fixturePath = process.env.DALAN_ANALYTICS_TEST_FIXTURE;
    if (!fixturePath) throw new Error("Set DALAN_ANALYTICS_TEST_FIXTURE to a disposable analytics fixture manifest.");
    const fixture = JSON.parse(fs.readFileSync(fixturePath, "utf8").replace(/^\uFEFF/, ""));
    const base = process.env.DALAN_ANALYTICS_TEST_BASE_URL || "http://127.0.0.1:5185";
    const output = process.env.DALAN_ANALYTICS_TEST_OUTPUT || path.join(os.tmpdir(), "dalan-analytics-browser");
    fs.mkdirSync(output, { recursive: true });
    const browser = await chromium.launch({ headless: true, channel: process.env.DALAN_BROWSER_CHANNEL });
    const errors = [];
    const context = await browser.newContext({ viewport: { width: 1440, height: 1100 } });
    const page = await context.newPage();
    page.on("pageerror", error => errors.push(error.message));
    const reportUrl = base + "/Analytics?from=" + fixture.from + "&to=" + fixture.to;
    async function login(target, email) {
        await target.goto(base + "/Account/Login");
        await target.locator("#Email").fill(email);
        await target.locator("#Password").fill(fixture.credential);
        await Promise.all([target.waitForURL(url => !url.pathname.includes("/Account/Login")),
            target.locator('button[type="submit"]').click()]);
    }
    async function metric(target, key, expected) {
        assert.equal((await target.locator('[data-metric="' + key + '"]').innerText()).trim(), expected);
    }
    try {
        await login(page, fixture.adminEmail);
        await page.goto(reportUrl);
        await metric(page, "open", "4");
        await metric(page, "completed", "3");
        await metric(page, "overdue", "1");
        await metric(page, "duration", "3.0 hr");
        await metric(page, "on-time", "50.0%");
        await metric(page, "participation", "33.3%");
        assert.ok(await page.getByRole("navigation", { name: "Main navigation" }).getByRole("link", { name: "Analytics", exact: true }).isVisible());
        assert.ok(await page.getByRole("table", { name: "Vehicle dispatch participation" }).getByRole("row").filter({ hasText: "ANALYTICS-1" }).isVisible());
        await page.locator("#main-content").screenshot({ path: path.join(output, "analytics-light.png") });
        console.log("PASS: Admin report matches actual SQL fixture counts, duration, deadline and fleet denominators");

        await page.getByText("View daily counts", { exact: true }).click();
        assert.equal(await page.locator(".analytics-daily-table tbody tr").count(), 3);
        await page.locator("#theme-select").selectOption("dark");
        await page.waitForFunction(() => getComputedStyle(document.querySelector(".content-card")).backgroundColor === "rgb(35, 38, 43)");
        await page.waitForFunction(() => getComputedStyle(document.querySelector(".analytics-filter .btn-primary")).color === "rgb(37, 28, 19)");
        await page.locator("#main-content").screenshot({ path: path.join(output, "analytics-dark.png") });
        await page.emulateMedia({ colorScheme: "light" });
        await page.locator("#theme-select").selectOption("system");
        await page.waitForFunction(() => getComputedStyle(document.querySelector(".content-card")).backgroundColor === "rgb(255, 255, 255)");
        await page.waitForFunction(() => getComputedStyle(document.querySelector(".analytics-filter .btn-primary")).color === "rgb(255, 255, 255)");
        await page.setViewportSize({ width: 390, height: 844 });
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 2));
        await page.locator("#main-content").screenshot({ path: path.join(output, "analytics-mobile.png") });
        console.log("PASS: exact daily values, Light/Dark/System and narrow viewport");

        await page.setViewportSize({ width: 1440, height: 1100 });
        await page.locator("#From").fill("2020-01-01");
        await page.locator("#To").fill("2020-01-02");
        await page.getByRole("button", { name: "Apply period", exact: true }).click();
        assert.match(await page.locator("#main-content").innerText(), /No deliveries were created/);
        await metric(page, "duration", "—");
        await metric(page, "on-time", "—");
        await metric(page, "participation", "0.0%");
        await page.locator("#main-content").screenshot({ path: path.join(output, "analytics-empty.png") });
        await page.locator("#From").fill("2020-01-01");
        await page.locator("#To").fill("2026-11-17");
        await page.getByRole("button", { name: "Apply period", exact: true }).click();
        assert.match(await page.locator(".validation-summary-errors").innerText(), /at most 366 days/);
        assert.equal(await page.locator("[data-analytics-report]").count(), 0);
        console.log("PASS: GET filters, truthful empty states and invalid-range handling");

        const staffContext = await browser.newContext();
        const staff = await staffContext.newPage();
        await login(staff, fixture.dispatcherEmail);
        await staff.goto(reportUrl);
        await metric(staff, "completed", "3");
        await metric(staff, "participation", "33.3%");
        await staffContext.close();

        const anonymous = await browser.newContext();
        const denied = await anonymous.newPage();
        await denied.goto(base + "/Analytics");
        assert.ok(denied.url().includes("/Account/Login"));
        await anonymous.close();
        assert.deepEqual(errors, []);
        console.log("PASS: Dispatcher access, anonymous sign-in challenge and no uncaught browser errors");
        console.log("Screenshots: " + output);
    } catch (error) {
        await page.screenshot({ path: path.join(output, "failure.png"), fullPage: true });
        console.error(JSON.stringify({ page: page.url(), errors }));
        throw error;
    } finally { await browser.close(); }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
