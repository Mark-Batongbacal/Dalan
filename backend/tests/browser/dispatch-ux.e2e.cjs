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
        await page.route('https://**/*', route => process.env.DALAN_REAL_BASEMAP === '1' && /^(tiles.openfreemap.org|fonts.googleapis.com|fonts.gstatic.com)$/.test(new URL(route.request().url()).hostname) ? route.continue() : route.abort());
        await page.goto(base + '/Account/Login');
        await page.locator('#Email').fill(fixture.email);
        await page.locator('#Password').fill(fixture.password);
        await Promise.all([page.waitForURL(/\/(Vehicles|DispatchBoard)$/), page.getByRole('button', { name: /^Sign in(?: to workspace)?$/ }).click()]);
        async function submit(name) {
            await Promise.all([page.waitForNavigation(), page.getByRole('button', { name, exact: true }).click()]);
        }
        const accessibility = [];
        async function capture(stage) {
            for (const width of [390, 1440]) for (const theme of ['light', 'dark', 'system']) {
                await page.setViewportSize({ width, height: 1000 });
                await page.emulateMedia({ colorScheme: theme === 'system' ? 'dark' : theme });
                await page.locator('#theme-select').selectOption(theme);
                await page.waitForTimeout(250); // Inspect the settled theme, after its short color transition.
                if (process.env.DALAN_AXE_MODULE) {
                    await page.addScriptTag({ content: fs.readFileSync(process.env.DALAN_AXE_MODULE, 'utf8') });
                    const issues = await page.evaluate(async () => (await axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a','wcag2aa','wcag21a','wcag21aa'] } })).violations.map(v => ({id:v.id,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))})));
                    accessibility.push({stage,width,theme,violations:issues}); fs.writeFileSync(path.join(output,'workflow-accessibility.json'),JSON.stringify(accessibility,null,2));
                    assert.deepEqual(issues, [], 'Dispatch workflow accessibility');
                }
                if (process.env.DALAN_REAL_BASEMAP === '1') await page.waitForTimeout(2000);
                await page.waitForFunction(() => {
                    const button = document.querySelector('.btn-primary:not(:disabled)'); if (!button) return true;
                    const probe = document.createElement('span'); probe.style.color = getComputedStyle(document.documentElement).getPropertyValue('--on-primary'); document.body.append(probe);
                    const expected = getComputedStyle(probe).color; probe.remove(); return getComputedStyle(button).color === expected;
                });
                assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), stage + ' fits ' + width);
                assert.ok(await page.evaluate(() => { const ids = [...document.querySelectorAll('[id]')].map(e => e.id); return new Set(ids).size === ids.length; }), stage + ' IDs unique');
                await page.screenshot({ path: path.join(output, `${stage}-${width}-${theme}.png`), fullPage: true });
            }
        }
        await page.goto(base + '/Deliveries');
        await capture('delivery-queue');
        await page.goto(base + '/FleetPlanner?mode=advanced');
        await page.route('**/PlanningMap/Addresses', route => route.fulfill({ json: {
            results: [{ address: '123 UX Street' }]
        } }));
        async function chooseAddress() {
            await page.locator('[data-google-open]').click();
            await page.locator('[data-map-search]').fill('123 UX Street');
            await page.locator('[data-map-search-button]').click();
            await page.locator('[data-address-intended]').check();
            await page.getByRole('button', { name: '123 UX Street', exact: true }).click();
        }
        async function chooseLocation(target) {
            await page.locator('[data-map-target]').selectOption(target);
            await chooseAddress();
            await page.locator('[data-map-advanced]').click();
            const ids = await page.locator('[data-planning-map]').evaluate(root => {
                const model = JSON.parse(root.querySelector('[data-map-model]').textContent).model;
                const target = root.querySelector('[data-map-target]').value;
                return model.targets[Number(target)] || {latitudeInputId:'Jobs_1__' + (target.endsWith('0') ? 'Pickup' : 'Dropoff') + 'Latitude'};
            });
            // Dynamic jobs are represented by the option label rather than the initial JSON.
            const label = await page.locator('[data-map-target] option:checked').textContent();
            let latitude = ids.latitudeInputId, longitude = ids.longitudeInputId;
            if (label.includes('Job 2')) { const kind = label.includes('pickup') ? 'Pickup' : 'Dropoff'; latitude = `Jobs_1__${kind}Latitude`; longitude = `Jobs_1__${kind}Longitude`; }
            await page.locator('#' + latitude).fill('14.600000');
            await page.locator('#' + longitude).fill('121.000000');
            if (ids.addressInputId) await page.locator('#' + ids.addressInputId).fill('123 UX Street');
            await page.locator('[data-map-coordinate-fields][open] summary').first().click();
            const selected = await page.locator('[data-map-target]').inputValue();
            await page.locator('[data-map-target]').selectOption(selected);
            await page.locator('[data-map-use-center]').click();
            await page.locator('[data-map-confirm]').click();
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
        await chooseAddress();
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
        await page.route('**/PlanningMap/Addresses', route => route.fulfill({ status: 503, json: { error: 'Search unavailable. Use Advanced coordinates.' } }));
        await page.locator('[data-google-open]').click();
        await page.locator('[data-map-search]').fill('Unavailable');
        await page.locator('[data-map-search-button]').click();
        await page.waitForFunction(() => !document.querySelector('[data-map-search-button]').disabled);
        assert.equal(await page.locator('#DropoffLatitude').inputValue(), '14.600000');
        assert.equal(await page.locator('#RecipientName').inputValue(), 'Location workflow');
        await page.locator('[data-google-close]').click();
        await page.locator('[data-map-advanced]').click();
        assert.ok(await page.locator('#DropoffLatitude').evaluate(input => document.activeElement === input && input.closest('details').open));
        await page.locator('#PayloadWeightKg').fill('2');
        await submit('Create delivery');
        assert.match(await page.locator('#main-content').innerText(), /Location workflow/);
        await page.setViewportSize({width:1440,height:1000});
        await page.goto(base + '/DispatchBoard');
        await page.locator('.dispatch-vehicles').evaluate(x=>x.open=true);
        await page.locator('.dispatch-options').evaluate(x=>x.open=true);
        const mapCanvas = page.locator('.maplibregl-canvas');
        const mapBounds = await mapCanvas.boundingBox();
        await mapCanvas.click({position:{x:mapBounds.width / 2,y:mapBounds.height / 2}});
        assert.ok(await page.locator('.planning-map-tools').evaluate(x=>x.open), 'A map choice reveals its explicit confirmation controls');
        await page.locator('[data-map-advanced]').click();
        assert.ok(await page.locator('#Settings_WarehouseLatitude').isVisible(), 'Advanced fallback opens all enclosing settings panels');
        await page.locator('.dispatch-options details[open] > summary').click();
        await page.locator('.planning-map-tools > summary').click();
        assert.equal(matrices, 0);
        const vehicle = page.locator('input[name$=".Id"][value="' + fixture.vehicleId + '"]').first();
        const prefix = (await vehicle.getAttribute('name')).replace('.Id', '');
        await page.locator(`[name="${prefix}.Selected"][type=checkbox]`).check();
        await page.locator(`[name="${prefix}.ExistingWorkloadSeconds"]`).fill('600');
        await page.getByRole('checkbox', { name: /UX Recipient/ }).check();
        await page.locator('[data-assignment-search]').fill('no-matching-reference-delivery');
        assert.equal(await page.locator('[data-assignment-card]:visible').count(), 0);
        await page.locator('[data-assignment-search]').fill('');
        assert.ok(await page.getByRole('checkbox', { name: /UX Recipient/ }).isChecked(), 'Filtering keeps selected deliveries');
        await page.locator('[data-delivery-filter="Assigned"]').click();
        assert.equal(await page.locator('[data-delivery-filter="Assigned"]').getAttribute('aria-pressed'), 'true');
        await page.locator('[data-delivery-filter="all"]').click();
        assert.equal(await page.locator('#Settings_WarehouseLatitude').inputValue(), '14.6');
        assert.equal(await page.locator('#Settings_WarehouseAddress').inputValue(), '123 UX Street');
        await page.locator('#Strategy').selectOption('WorkloadEquity');
        await page.locator('#Settings_ServiceSecondsPerStop').fill('60');
        await submit('Suggest strategy without routing');
        assert.equal(matrices, 0);
        assert.equal(await page.locator('#Strategy').inputValue(), 'WorkloadEquity');
        fail = true;
        await submit('Preview routes');
        assert.match(await page.locator('[role=alert]').first().innerText(), /routing provider failed/i);
        assert.equal(await page.locator('#Settings_WarehouseAddress').inputValue(), '123 UX Street');
        fail = false;
        await submit('Preview routes');
        assert.equal(matrices, 1);
        await page.getByRole('heading', { name: 'Route preview' }).waitFor();
        await capture('preview');
        await submit('Review this plan');
        assert.equal(matrices, 2);
        await capture('review');
        const beforeReturn = matrices;
        await submit('Back to selections (new preview required)');
        assert.equal(matrices, beforeReturn);
        assert.equal(await page.locator('#Strategy').inputValue(), 'WorkloadEquity');
        assert.equal(await page.locator('#Settings_WarehouseAddress').inputValue(), '123 UX Street');
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
