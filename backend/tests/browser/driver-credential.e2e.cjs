const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const { chromium } = require(process.env.DALAN_PLAYWRIGHT_MODULE || 'playwright');
(async () => {
    const fixture = JSON.parse(fs.readFileSync(0, 'utf8'));
    const root = path.resolve(__dirname, '../../src/Dalan.Web');
    const base = 'http://127.0.0.1:5197';
    const server = spawn('dotnet', [path.join(root, 'bin/Debug/net9.0/Dalan.Web.dll'), '--urls', base], {
        cwd: root, stdio: 'ignore', env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Testing',
            ConnectionStrings__Dalan: fixture.connection, DALAN_ADMIN_PASSWORD: '',
            DriverInvitations__Enabled: 'true', DriverInvitations__PublicBaseUrl: 'https://dalan.test',
            DriverRecovery__Enabled: 'true', DriverRecovery__PublicBaseUrl: 'https://dalan.test',
            DriverCredentialEmail__Enabled: 'true', DriverCredentialEmail__KeyRingPath: fixture.keys,
            Resend__Enabled: 'true', Resend__ApiKey: 'fake', Resend__From: 'test@dalan.test',
            Resend__Recipients: 'test@dalan.test', AccountAudit__Enabled: 'true' }
    });
    let browser;
    try {
        let ready = false;
        for (let i = 0; i < 100; i++) {
            if (server.exitCode !== null) throw new Error('Credential test host exited');
            try { ready = (await fetch(base + '/Account/Login')).ok; } catch {}
            if (ready) break;
            await new Promise(resolve => setTimeout(resolve, 200));
        }
        assert.ok(ready);
        browser = await chromium.launch({ headless: true, channel: 'msedge' });
        const page = await browser.newPage({ viewport: { width: 390, height: 844 }, colorScheme: 'dark' });
        const errors = [];
        page.on('pageerror', () => errors.push('Page script failed'));
        const route = fixture.activate ? '/DriverInvitations/Activate' : '/DriverRecovery/Reset';
        const missing = await page.goto(base + route);
        assert.equal(missing.status(), 200);
        assert.equal(missing.headers()['referrer-policy'], 'no-referrer');
        assert.match(missing.headers()['cache-control'], /no-store/);
        assert.equal(await page.locator('button[type=submit]').isDisabled(), true);
        assert.match(await page.locator('#credential-status').innerText(), /incomplete/);
        await page.goto('about:blank');
        await page.goto(base + route + '#' + new URLSearchParams({ email: fixture.email, token: fixture.token }));
        await page.waitForFunction(() => !location.hash);
        assert.equal(await page.evaluate(() => location.search), '');
        await page.locator('#new-password').fill(fixture.password);
        await page.locator('#confirm-password').fill('Different-Password!2026');
        await page.locator('button[type=submit]').click();
        await page.waitForFunction(() => document.getElementById('credential-status').textContent.includes('must match'));
        await page.locator('#confirm-password').fill(fixture.password);
        const submitted = page.waitForResponse(r => r.url().includes('/api/driver-') && r.request().method() === 'POST');
        await page.locator('button[type=submit]').click();
        assert.equal((await submitted).status(), 200);
        await page.waitForFunction(() => document.getElementById('driver-credential').hidden);
        assert.match(await page.locator('#credential-status').innerText(), /Sign in/);
        assert.equal(await page.locator('#new-password').inputValue(), '');
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
        assert.deepEqual(errors, []);
        console.log('Credential browser journey passed');
    } finally {
        if (browser) await browser.close();
        server.kill();
        await new Promise(resolve => server.exitCode !== null ? resolve() : server.once('exit', resolve));
    }
})().catch(() => { console.error('Credential browser journey failed'); process.exitCode = 1; });
