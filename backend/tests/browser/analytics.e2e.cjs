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
        assert.deepEqual(await page.locator(".analytics-daily-table tbody td").allTextContents(), ["3", "2", "3"]);
        assert.match(await page.getByLabel("Applied reporting period").innerText(), /3 inclusive calendar days/);
        await page.getByText("How to read this report", { exact: true }).click();
        assert.match(await page.locator(".analytics-definitions").innerText(), /not historical statuses/);
        assert.match(await page.locator(".analytics-definitions").innerText(), /not measure GPS activity/);
        await page.locator("#theme-select").selectOption("dark");
        await page.waitForFunction(() => getComputedStyle(document.querySelector(".content-card")).backgroundColor === "rgb(35, 38, 43)");
        await page.waitForFunction(() => {
                const button = document.querySelector('.btn-primary'); if (!button) return true;
                const probe = document.createElement('span'); probe.style.color = getComputedStyle(document.documentElement).getPropertyValue('--on-primary'); document.body.append(probe);
                const expected = getComputedStyle(probe).color; probe.remove(); return getComputedStyle(button).color === expected;
            });
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

        const through = new Date(fixture.to + "T00:00:00Z");
        const from = new Date(through); from.setUTCDate(from.getUTCDate() - 365);
        await page.goto(base + "/Analytics?from=" + from.toISOString().slice(0, 10) + "&to=" + fixture.to);
        assert.equal(await page.locator(".analytics-chart-column").count(), 366);
        assert.match(await page.getByLabel("Applied reporting period").innerText(), /366 inclusive calendar days/);
        const chart = page.locator(".analytics-chart-scroll");
        await chart.focus();
        await page.keyboard.press("ArrowRight");
        await page.waitForFunction(() => document.querySelector(".analytics-chart-scroll").scrollLeft > 0);
        await page.getByText("View daily counts", { exact: true }).click();
        assert.equal(await page.locator(".analytics-daily-table tbody tr").count(), 366);
        await page.getByRole("link", { name: "Daily intake", exact: true }).click();
        assert.equal(await page.evaluate(() => document.activeElement.id), "daily-intake");
        await page.setViewportSize({ width: 390, height: 844 });
        const vehicleTable = page.locator(".analytics-vehicle-table");
        await vehicleTable.focus();
        await page.keyboard.press("ArrowRight");
        await page.waitForFunction(() => document.querySelector(".analytics-vehicle-table").scrollLeft > 0);
        for (const theme of ["light", "dark", "system"]) {
            await page.locator("#theme-select").selectOption(theme);
            if (theme === "system") await page.emulateMedia({ colorScheme: "dark" });
            assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 2));
            await page.locator("#main-content").screenshot({ path: path.join(output, "analytics-long-mobile-" + theme + ".png") });
        }
        await page.setViewportSize({ width: 1440, height: 1100 });
        await page.locator("#main-content").screenshot({ path: path.join(output, "analytics-long-desktop.png") });
        console.log("PASS: 366-day exact data alternative, keyboard chart/table scrolling, section focus and mobile themes");

        // Cancel one navigation after the real submit handler runs so its transient UI can be inspected.
        await page.locator("[data-analytics-filter]").evaluate(form => form.addEventListener("submit", event => event.preventDefault(), { once: true }));
        await page.getByRole("button", { name: "Apply period", exact: true }).click();
        assert.match(await page.locator("[data-analytics-loading]").innerText(), /Loading the selected period/);
        assert.equal(await page.locator("[data-analytics-filter]").getAttribute("aria-busy"), "true");
        assert.ok(await page.getByRole("button", { name: "Apply period", exact: true }).isDisabled());
        await page.reload();
        assert.ok(await page.getByRole("button", { name: "Apply period", exact: true }).isEnabled());
        await page.getByRole("link", { name: "Reset to last 30 UTC days", exact: true }).click();
        await page.waitForURL(url => url.pathname === "/Analytics" && url.search === "");
        assert.match(await page.getByLabel("Applied reporting period").innerText(), /30 inclusive calendar days/);
        console.log("PASS: loading announcement, duplicate-submit protection, reset and ready state");

        const noScriptContext = await browser.newContext({ javaScriptEnabled: false });
        const noScript = await noScriptContext.newPage();
        await login(noScript, fixture.adminEmail);
        await noScript.goto(reportUrl);
        await metric(noScript, "completed", "3");
        await noScript.locator("#From").fill("2020-01-01");
        await noScript.locator("#To").fill("2020-01-02");
        await noScript.getByRole("button", { name: "Apply period", exact: true }).click();
        assert.match(await noScript.locator("#main-content").innerText(), /No deliveries were created/);
        await noScriptContext.close();
        console.log("PASS: report and normal GET filtering work without JavaScript");

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
