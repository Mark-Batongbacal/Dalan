const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const { chromium } = require(process.env.DALAN_PLAYWRIGHT_MODULE || 'playwright');
(async () => {
    const fixture = JSON.parse(fs.readFileSync(0, 'utf8'));
    let matrices = 0, fail = false;
    const provider = http.createServer(async (req, res) => {
        let raw = ''; for await (const chunk of req) raw += chunk;
        const body = JSON.parse(raw);
        res.setHeader('Content-Type', 'application/json');
        if (fail) { res.writeHead(503); res.end('{}'); return; }
        if (req.url.includes('sources_to_targets')) {
            matrices++;
            res.end(JSON.stringify({ sources_to_targets: {
                durations: body.sources.map(a => body.targets.map(b => a.lat === b.lat && a.lon === b.lon ? 0 : 120)),
                distances: body.sources.map(a => body.targets.map(b => a.lat === b.lat && a.lon === b.lon ? 0 : 1))
            } }));
        } else res.end(JSON.stringify({ trip: { summary: { length: 1, time: 120 }, legs: [{ shape: '_ibE_seK_seK_seK' }] } }));
    });
    await new Promise(resolve => provider.listen(0, '127.0.0.1', resolve));
    const root = path.resolve(__dirname, '../../src/Dalan.Web');
    const base = 'http://127.0.0.1:5197';
    const output = process.env.DALAN_BROWSER_OUTPUT || path.join(require('os').tmpdir(), 'dalan-dispatch-ux');
    fs.mkdirSync(output, { recursive: true });
    const server = spawn('dotnet', [path.join(root, 'bin/Debug/net9.0/Dalan.Web.dll'), '--urls', base], {
        cwd: root, stdio: 'ignore', env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Testing', DALAN_ADMIN_PASSWORD: '',
            ConnectionStrings__Dalan: fixture.connection, Valhalla__BaseUrl: `http://127.0.0.1:${provider.address().port}/` }
    });
    let browser;
    try {
        let ready = false;
        for (let i = 0; i < 100; i++) {
            if (server.exitCode !== null) throw Error('MVC server exited');
            try { ready = (await fetch(base + '/Account/Login')).ok; } catch {}
            if (ready) break; await new Promise(resolve => setTimeout(resolve, 200));
        }
        assert.ok(ready);
        browser = await chromium.launch({ channel: 'msedge', headless: true });
        const page = await browser.newPage();
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.route('https://**/*', route => route.abort());
        await page.goto(base + '/Account/Login');
        await page.locator('#Email').fill(fixture.email);
        await page.locator('#Password').fill(fixture.password);
        await Promise.all([page.waitForURL('**/Vehicles'), page.getByRole('button', { name: 'Sign in', exact: true }).click()]);
        async function submit(name) {
            await Promise.all([page.waitForNavigation(), page.getByRole('button', { name, exact: true }).click()]);
        }
        async function capture(stage) {
            for (const width of [390, 1440]) for (const theme of ['light', 'dark', 'system']) {
                await page.setViewportSize({ width, height: 1000 });
                await page.emulateMedia({ colorScheme: theme === 'system' ? 'dark' : theme });
                await page.locator('#theme-select').selectOption(theme);
                await page.waitForFunction(expected => { const button = document.querySelector('.btn-primary:not(:disabled)'); return !button || getComputedStyle(button).color === expected; },
                    theme === 'light' ? 'rgb(255, 255, 255)' : 'rgb(37, 28, 19)');
                assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), stage + ' fits ' + width);
                assert.ok(await page.evaluate(() => { const ids = [...document.querySelectorAll('[id]')].map(e => e.id); return new Set(ids).size === ids.length; }), stage + ' IDs unique');
                await page.screenshot({ path: path.join(output, `${stage}-${width}-${theme}.png`), fullPage: true });
            }
        }
        await page.goto(base + '/Deliveries');
        await capture('delivery-queue');
        await page.goto(base + '/FleetPlanner');
        await page.route('**/PlanningMap/Search', route => route.fulfill({ json: {
            results: [{ label: 'UX warehouse', location: { latitude: 14.6, longitude: 121 } }], attribution: 'Photon / OpenStreetMap'
        } }));
        async function chooseLocation(target) {
            await page.locator('[data-map-target]').selectOption(target);
            await page.locator('[data-map-search]').fill('Manila');
            await page.locator('[data-map-search-button]').click();
            await page.getByRole('button', { name: 'UX warehouse', exact: true }).click();
            await page.locator('[data-map-confirm]').focus();
            await page.keyboard.press('Enter');
        }
        assert.equal(await page.locator('[data-map-coordinate-fields][open]').count(), 0);
        const startTarget = await page.locator('[data-map-target] option').allTextContents();
        await chooseLocation({ label: startTarget[0] });
        assert.equal(await page.locator('#Vehicles_0__StartLatitude').inputValue(), '14.600000');
        await chooseLocation({ label: 'Job 1 pickup' });
        await chooseLocation({ label: 'Job 1 drop-off' });
        await page.getByRole('button', { name: 'Add job', exact: true }).click();
        await chooseLocation({ label: 'Job 2 pickup' });
        await chooseLocation({ label: 'Job 2 drop-off' });
        assert.equal(await page.locator('#Jobs_1__DropoffLatitude').inputValue(), '14.600000');
        await capture('fleet-planner');
        await page.goto(base + '/Deliveries/Create');
        await page.locator('#RecipientName').fill('Location workflow');
        await chooseLocation('0');
        await page.locator('[data-map-target]').selectOption('1');
        await page.locator('[data-map-search]').fill('Manila');
        await page.locator('[data-map-search-button]').click();
        await page.getByRole('button', { name: 'UX warehouse', exact: true }).click();
        assert.equal(await page.locator('#DropoffLatitude').inputValue(), '');
        assert.equal(await page.locator('[data-planning-map]').evaluate(root =>
            root.closest('form').dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }))), false);
        await page.locator('[data-map-target]').selectOption('0');
        assert.ok(await page.locator('[data-map-confirm]').isDisabled());
        assert.equal(await page.locator('[data-map-results] button').count(), 0);
        await chooseLocation('1');
        assert.equal(await page.locator('#DropoffLatitude').inputValue(), '14.600000');
        assert.doesNotMatch(await page.locator('[data-valmsg-summary]').innerText(), /Dropoff/);
        await capture('delivery-location');
        await page.route('**/PlanningMap/Search', route => route.fulfill({ status: 503, json: { error: 'Search unavailable. Use Advanced coordinates.' } }));
        await page.locator('[data-map-search]').fill('Unavailable');
        await page.locator('[data-map-search-button]').click();
        await page.waitForFunction(() => !document.querySelector('[data-map-search-button]').disabled);
        assert.equal(await page.locator('#DropoffLatitude').inputValue(), '14.600000');
        assert.equal(await page.locator('#RecipientName').inputValue(), 'Location workflow');
        await page.locator('[data-map-advanced]').click();
        assert.ok(await page.locator('#DropoffLatitude').evaluate(input => document.activeElement === input && input.closest('details').open));
        await page.locator('#PayloadWeightKg').fill('2');
        await submit('Create delivery');
        assert.match(await page.locator('#main-content').innerText(), /Location workflow/);
        await page.goto(base + '/DispatchBoard');
        assert.equal(matrices, 0);
        const vehicle = page.locator('input[name$=".Id"][value="' + fixture.vehicleId + '"]').first();
        const prefix = (await vehicle.getAttribute('name')).replace('.Id', '');
        await page.locator(`[name="${prefix}.Selected"][type=checkbox]`).check();
        await page.locator(`[name="${prefix}.ExistingWorkloadSeconds"]`).fill('600');
        await page.getByLabel(/UX Recipient/).check();
        assert.equal(await page.locator('#Settings_WarehouseLatitude').inputValue(), '14.6');
        assert.equal(await page.locator('#Settings_WarehouseAddress').inputValue(), 'UX warehouse');
        await page.locator('#Strategy').selectOption('WorkloadEquity');
        await page.locator('#Settings_ServiceSecondsPerStop').fill('60');
        await submit('Suggest strategy without routing');
        assert.equal(matrices, 0);
        assert.equal(await page.locator('#Strategy').inputValue(), 'WorkloadEquity');
        fail = true;
        await submit('Preview routes');
        assert.match(await page.locator('[role=alert]').first().innerText(), /routing provider failed/i);
        assert.equal(await page.locator('#Settings_WarehouseAddress').inputValue(), 'UX warehouse');
        fail = false;
        await submit('Preview routes');
        assert.equal(matrices, 1);
        await page.getByRole('heading', { name: 'Selected-plan results' }).waitFor();
        await capture('preview');
        await submit('Review this plan');
        assert.equal(matrices, 2);
        await capture('review');
        const beforeReturn = matrices;
        await submit('Change selections (new preview required)');
        assert.equal(matrices, beforeReturn);
        assert.equal(await page.locator('#Strategy').inputValue(), 'WorkloadEquity');
        assert.equal(await page.locator('#Settings_WarehouseAddress').inputValue(), 'UX warehouse');
        assert.equal(await page.locator(`[name="${prefix}.ExistingWorkloadSeconds"]`).inputValue(), '600');
        assert.ok(await page.locator(`[name="${prefix}.Selected"][type=checkbox]`).isChecked());
        await submit('Preview routes');
        await submit('Review this plan');
        fail = true;
        await submit('Confirm dispatch');
        assert.match(await page.locator('[data-dispatch-error]').innerText(), /routing provider failed/i);
        assert.equal(await page.locator('[name="Vehicles[0].ExistingWorkloadSeconds"]').inputValue(), '600');
        fail = false;
        const oldConfirmation = await page.locator('form[action="/DispatchBoard/Confirm"]').evaluate(form => Object.fromEntries(new FormData(form)));
        await page.getByRole('button', { name: 'Confirm dispatch', exact: true }).focus();
        await Promise.all([page.waitForURL('**/DispatchBoard/Plan/*'), page.keyboard.press('Enter')]);
        assert.match(await page.locator('#main-content').innerText(), /UX Recipient/);
        await capture('confirmed');
        const stale = await page.request.post(base + '/DispatchBoard/Confirm', { form: oldConfirmation });
        assert.equal(stale.status(), 200);
        assert.match(await stale.text(), /stale|already been dispatched/i);
        assert.deepEqual(errors, []);
        console.log('PASS real Dispatcher login, SQL plan confirmation, no-routing selection/suggestion, provider failure recovery, retained inputs, themes and viewport matrix');
    } finally {
        if (browser) await browser.close();
        server.kill(); provider.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
