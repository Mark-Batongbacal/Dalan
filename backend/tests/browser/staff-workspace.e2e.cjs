const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const { chromium } = require(process.env.DALAN_PLAYWRIGHT_MODULE || 'playwright');

(async () => {
    const fixture = JSON.parse(fs.readFileSync(0, 'utf8'));
    const root = path.resolve(__dirname, '../../src/Dalan.Web');
    const base = 'http://127.0.0.1:5196';
    const output = process.env.DALAN_STAFF_OUTPUT || path.join(os.tmpdir(), 'dalan-staff-browser');
    fs.mkdirSync(output, { recursive: true });
    const server = spawn('dotnet', [path.join(root, 'bin/Debug/net9.0/Dalan.Web.dll'), '--urls', base], {
        cwd: root, stdio: 'ignore', env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Testing',
            ConnectionStrings__Dalan: fixture.connection, DALAN_ADMIN_PASSWORD: '' }
    });
    let browser;
    try {
        let ready = false;
        for (let i = 0; i < 100; i++) {
            if (server.exitCode !== null) throw new Error('MVC verification server exited');
            try { ready = (await fetch(base + '/Account/Login')).ok; } catch {}
            if (ready) break;
            await new Promise(resolve => setTimeout(resolve, 200));
        }
        assert.ok(ready, 'MVC verification server starts');
        browser = await chromium.launch({ headless: true, channel: 'msedge' });
        const page = await browser.newPage();
        const errors = [];
        page.on('pageerror', e => errors.push(e.message));
        page.on('response', response => { if (response.status() >= 400 && /\.(css|js)(\?|$)/.test(response.url())) errors.push(response.status() + ' ' + response.url()); });
        await page.goto(base + '/Account/Login');
        await page.keyboard.press('Tab');
        assert.ok(await page.locator('.skip-link').evaluate(e => e === document.activeElement));
        await page.keyboard.press('Enter');
        await page.keyboard.press('Tab');
        assert.ok(await page.locator('.auth-logo').evaluate(e => e === document.activeElement));
        await page.keyboard.press('Tab');
        assert.ok(await page.locator('#Email').evaluate(e => e === document.activeElement));
        await page.keyboard.type(fixture.email);
        await page.keyboard.press('Tab');
        await page.keyboard.type(fixture.password);
        await page.keyboard.press('Tab');
        await page.keyboard.press('Tab');
        await Promise.all([page.waitForURL('**/Vehicles'), page.keyboard.press('Enter')]);
        const routes = fixture.role === 'Admin' ? ['/Vehicles', '/Vehicles/Create', '/Drivers', '/Drivers/Create', '/StaffAccounts/Create', '/Vehicles/Edit/' + fixture.vehicleId, '/Drivers/Edit/' + fixture.driverId] : ['/Vehicles'];
        for (const width of [390, 1440]) for (const theme of ['light', 'dark', 'system']) for (const route of routes) {
            await page.setViewportSize({ width, height: 1000 });
            await page.emulateMedia({ colorScheme: theme === 'system' ? 'dark' : theme });
            await page.goto(base + route);
            await page.locator('#theme-select').selectOption(theme);
            assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `${route} fits ${width} ${theme}`);
            assert.equal(await page.locator('h1').count(), 1);
            const primary = page.locator('.btn-primary').first();
            if (await primary.count()) await page.waitForFunction(() => {
                const button = document.querySelector('.btn-primary'); if (!button) return true;
                const probe = document.createElement('span'); probe.style.color = getComputedStyle(document.documentElement).getPropertyValue('--on-primary'); document.body.append(probe);
                const expected = getComputedStyle(probe).color; probe.remove(); return getComputedStyle(button).color === expected;
            });
            assert.equal(await page.evaluate(() => {
                const ids = [...document.querySelectorAll('[id]')].map(e => e.id);
                return new Set(ids).size === ids.length;
            }), true, 'unique element IDs');
            assert.equal(await page.locator('link[rel=stylesheet][href*="/css/site.css"]').count(), 1);
            assert.equal(await page.locator('link[rel=stylesheet][href*="/lib/bootstrap/"]').count(), 1);
            if (width === 390 && route === '/Vehicles') {
                await page.getByRole('button', { name: 'Toggle navigation' }).focus();
                await page.keyboard.press('Enter');
                assert.equal(await page.locator('.nav-toggle').getAttribute('aria-expanded'), 'true');
                assert.ok(await page.locator('.sidebar-nav a').first().evaluate(e => e === document.activeElement));
                assert.equal(await page.locator('.sidebar-nav a[href="/StaffAccounts/Create"]').count(), fixture.role === 'Admin' ? 1 : 0);
                await page.keyboard.press('Escape');
                assert.ok(await page.locator('.nav-toggle').evaluate(e => e === document.activeElement));
                assert.equal(await page.locator('.nav-toggle').getAttribute('aria-expanded'), 'false');
            }
            await page.screenshot({ path: path.join(output, `${fixture.role}-${route.replaceAll('/', '-')}-${width}-${theme}.png`) });
        }
        if (fixture.role === 'Admin') {
            await page.goto(base + '/Drivers/Create');
            await page.locator('#Name').fill('Preserved driver');
            await page.getByRole('button', { name: 'Add driver', exact: true }).click();
            assert.equal(await page.locator('#Name').inputValue(), 'Preserved driver');
            assert.ok(await page.locator('#Phone-validation').isVisible());
        }
        assert.deepEqual(errors, []);
        console.log(`PASS ${fixture.role}: actual cookie login, role navigation, mobile keyboard menu, forms, themes and widths`);
    } finally {
        if (browser) await browser.close();
        server.kill();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
