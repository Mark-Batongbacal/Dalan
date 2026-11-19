const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const { chromium } = require(process.env.DALAN_PLAYWRIGHT_MODULE || 'playwright');
(async () => {
 const fixture = JSON.parse(fs.readFileSync(0, 'utf8'));
 const root = path.resolve(__dirname, '../../src/Dalan.Web'), base = 'http://127.0.0.1:5198';
 const output = process.env.DALAN_UX_OUTPUT || path.join(require('node:os').tmpdir(), 'dalan-remaining-ux'); fs.mkdirSync(output, {recursive:true});
 const axe = fs.readFileSync(process.env.DALAN_AXE_MODULE, 'utf8');
 const server = spawn('dotnet', [path.join(root,'bin/Debug/net9.0/Dalan.Web.dll'),'--urls',base], {cwd:root,stdio:'ignore',env:{...process.env,
  ASPNETCORE_ENVIRONMENT:'Testing',ConnectionStrings__Dalan:fixture.connection,DALAN_ADMIN_PASSWORD:'',AccountAudit__Enabled:'true',
  DriverInvitations__Enabled:'true',DriverInvitations__PublicBaseUrl:'https://dalan.test',DriverRecovery__Enabled:'true',DriverRecovery__PublicBaseUrl:'https://dalan.test',
  DriverCredentialEmail__Enabled:'true',DriverCredentialEmail__KeyRingPath:fixture.keys,Resend__Enabled:'true',Resend__ApiKey:'fake',Resend__From:'fixture@dalan.test',Resend__Recipients:'fixture@dalan.test',DriverGpsProof__Enabled:'true'}});
 let browser; const scans = [];
 try {
  let ready = false; for (let i=0;i<100;i++) {try {ready=(await fetch(base+'/Account/Login')).ok;} catch {} if (ready) break; await new Promise(r=>setTimeout(r,200));} assert.ok(ready);
  browser = await chromium.launch({headless:true,channel:'msedge'});
  async function session(email) {
   const context = await browser.newContext({viewport:{width:390,height:844},reducedMotion:'reduce'});
   await context.route('**/tiles.openfreemap.org/**', route => route.fulfill({json:{version:8,sources:{},layers:[]}}));
   const page = await context.newPage();
   await page.goto(base+'/Account/Login');
   await page.locator('#Email').fill(email); await page.locator('#Password').fill('Wrong-Fixture-Password!2026');
   await page.getByRole('button',{name:/^Sign in(?: to workspace)?$/}).click();
   await page.getByRole('alert').filter({hasText:'Invalid sign-in attempt'}).waitFor({state:'visible'});
   await page.locator('#Password').fill(fixture.adminPassword);
   await Promise.all([page.waitForURL('**/Vehicles'),page.getByRole('button',{name:/^Sign in(?: to workspace)?$/}).click()]);
   return {context,page};
  }
  const admin = await session(fixture.adminEmail), page = admin.page;
  await page.goto(base+`/Drivers/${fixture.inviteDriverId}/Access`);
  await page.getByText('No linked mobile account',{exact:true}).waitFor();
  assert.equal(await page.locator('input[type=password]').count(),0);
  await page.locator('#invite-email').fill(fixture.inviteEmail);
  await page.getByRole('button',{name:'Send / reissue invitation'}).click();
  await page.getByText('Invitation pending activation',{exact:true}).waitFor();
  await page.locator('#access-reason').fill('Controlled browser access verification');
  page.once('dialog', d=>d.accept()); await page.getByRole('button',{name:'Disable access',exact:true}).click();
  await page.getByText('Mobile access disabled',{exact:true}).waitFor();
  await page.getByRole('button',{name:'Restore access',exact:true}).click();
  await page.getByText('Invitation pending activation',{exact:true}).waitFor();
  await page.goto(base+'/Drivers'); await page.locator(`[data-mobile-access-status="${fixture.inviteDriverId}"]`).getByText('Pending activation',{exact:true}).waitFor();
  await page.goto(base+'/DriverCredentialEmails'); await page.getByText('Pending / retrying',{exact:true}).first().waitFor();
  await page.goto(base+`/AccountAudit?driverId=${fixture.inviteDriverId}`); await page.getByRole('cell',{name:'DriverInvitationIssued',exact:true}).waitFor();
  const dispatcher = await session(fixture.dispatcherEmail);
  await dispatcher.page.goto(base+`/Drivers/${fixture.inviteDriverId}/Access`); assert.match(dispatcher.page.url(),/Account\/Denied/);
  await dispatcher.page.goto(base+'/GpsExceptions');
  const gps = dispatcher.page.locator('section').filter({has:dispatcher.page.getByRole('heading',{name:'GPS fixture recipient · Pending',exact:true})}).last();
  await gps.getByRole('heading',{name:'GPS fixture recipient · Pending',exact:true}).waitFor();
  await dispatcher.page.addScriptTag({content:axe});
  const pendingIssues = await dispatcher.page.evaluate(async()=> (await axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa']}})).violations.map(x=>({id:x.id,nodes:x.nodes.map(n=>({target:n.target,summary:n.failureSummary}))})));
  fs.writeFileSync(path.join(output,'pending-review-accessibility.json'),JSON.stringify(pendingIssues,null,2));
  assert.deepEqual(pendingIssues,[],'Pending GPS review accessibility');
  await dispatcher.page.locator(`#reason-${fixture.gpsReviewId}`).fill('Reviewed submitted photo and missing GPS evidence');
  dispatcher.page.once('dialog',d=>d.accept()); await dispatcher.page.getByRole('button',{name:'Approve exception and complete delivery',exact:true}).click();
  await dispatcher.page.getByRole('heading',{name:'GPS fixture recipient · Approved',exact:true}).waitFor();
  await dispatcher.page.goto(base+'/Deliveries/Create');
  await dispatcher.page.route('**/PlanningMap/Addresses', async route => {
   const request = route.request().postDataJSON();
   if (request.query.includes('Unavailable')) return route.fulfill({status:503,json:{title:'Fixture provider unavailable'}});
   return route.fulfill({json:{results:[{address:'123 Fixture Street'}]}});
  });
  await dispatcher.page.locator('[data-google-open]').click();
  await dispatcher.page.locator('[data-map-search]').fill('123 Fixture');
  await dispatcher.page.getByRole('button',{name:'123 Fixture Street',exact:true}).waitFor();
  assert.equal(await dispatcher.page.locator('#PickupLatitude').inputValue(),'');
  await dispatcher.page.addScriptTag({content:axe});
  const addressIssues = await dispatcher.page.evaluate(async()=> (await axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa']}})).violations.map(x=>({id:x.id,nodes:x.nodes.map(n=>({target:n.target,summary:n.failureSummary}))})));
  assert.deepEqual(addressIssues,[],'Address dialog accessibility');
  const backdrop = await dispatcher.page.locator('[data-google-dialog]').evaluate(x=>getComputedStyle(x,'::backdrop').backgroundColor);
  assert.notEqual(backdrop,'rgba(0, 0, 0, 0)','Address dialog backdrop must hide the map');
  await dispatcher.page.locator('[data-google-dialog]').screenshot({path:path.join(output,'street-address-dialog-mobile.png')});
  await dispatcher.page.locator('[data-address-intended]').check();
  await dispatcher.page.getByRole('button',{name:'123 Fixture Street',exact:true}).click();
  assert.equal(await dispatcher.page.locator('#PickupLatitude').inputValue(),'');
  await dispatcher.page.locator('[data-map-use-center]').click();
  await dispatcher.page.locator('[data-map-confirm]').click();
  assert.notEqual(await dispatcher.page.locator('#PickupLatitude').inputValue(),'');
  assert.equal(await dispatcher.page.locator('#PickupAddress').inputValue(),'123 Fixture Street');
  assert.equal(await dispatcher.page.evaluate(() => Object.values(localStorage).some(x=>x.includes('123 Fixture Street'))),false);
  await dispatcher.page.locator('[data-google-open]').click(); await dispatcher.page.locator('[data-map-search]').fill('Unavailable');
  await dispatcher.page.locator('[data-google-status]').getByText('Fixture provider unavailable',{exact:true}).waitFor();
  assert.equal(await dispatcher.page.locator('[data-map-search]').inputValue(),'Unavailable'); await dispatcher.page.locator('[data-google-close]').click();
  for (const [role,session,pages] of [
   ['Admin',admin,['/','/Vehicles','/Vehicles/Create',`/Vehicles/Edit/${fixture.vehicleId}`,'/Drivers','/Drivers/Create',`/Drivers/Edit/${fixture.inviteDriverId}`,`/Drivers/${fixture.inviteDriverId}/Access`,'/StaffAccounts/Create','/AccountAudit','/DriverCredentialEmails','/GpsExceptions','/Analytics','/FleetTracking','/Notifications','/Account/Login','/DriverRecovery/Request','/DriverRecovery/Reset','/DriverInvitations/Activate']],
   ['Dispatcher',dispatcher,['/','/Vehicles','/Deliveries','/Deliveries/Create',`/Deliveries/Details/${fixture.deliveryId}`,'/DispatchBoard','/FleetPlanner','/Analytics','/GpsExceptions','/FleetTracking','/Notifications','/Account/Login']]]) {
   const smoke = process.env.DALAN_UX_SMOKE === '1';
   for (const width of (smoke ? [390,1440] : [390,768,1440])) for (const theme of (smoke ? ['light','dark'] : ['light','dark','system'])) for (const route of pages.filter(route=>!smoke || ['/FleetPlanner','/Deliveries/Create'].includes(route))) {
    const page = session.page; await page.setViewportSize({width,height:900});
    const response = await page.goto(base+route); assert.equal(response.status(),200,`${role} ${route} must render successfully`); await page.locator('#theme-select').selectOption(theme);
    await page.waitForTimeout(150);
    if (width === 390 && !['/Account/Login','/DriverRecovery/Request','/DriverRecovery/Reset','/DriverInvitations/Activate'].includes(route)) {
     assert.ok(await page.locator('.nav-toggle').isVisible(), 'Mobile navigation is visible');
     assert.ok(await page.locator('.nav-toggle svg').evaluate(x=>x.getBoundingClientRect().width>=20),'Mobile navigation icon is visibly rendered');
     assert.ok(await page.locator('.nav-toggle').evaluate(x=>x.getBoundingClientRect().x>=0),'Mobile navigation is inside the viewport');
    }
    if (width === 1440 && await page.locator('.nav-toggle').count()) assert.equal(await page.locator('.nav-toggle').isVisible(),false,'Desktop uses the persistent sidebar');
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),true,`${role} ${route} ${width} ${theme} overflow`);
    await page.addScriptTag({content:axe});
    const result = await page.evaluate(async()=>{const result=await axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa']}});return result.violations.map(v=>({id:v.id,impact:v.impact,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))}));});
    scans.push({role,route,width,theme,violations:result});
    if (result.length) fs.writeFileSync(path.join(output,'accessibility-results.json'),JSON.stringify(scans,null,2));
    const slug = route.replace(/[0-9a-f]{8}-[0-9a-f-]{27,}/gi,'fixture').replaceAll('/','_') || '_home';
    await page.evaluate(()=>scrollTo(0,0));
    await page.screenshot({path:path.join(output,`${role}-${slug}-${width}-${theme}.png`),fullPage:!['/Vehicles','/Drivers','/Deliveries','/AccountAudit'].includes(route)});
   }
  }
  await dispatcher.page.goto(base+'/Deliveries/Create');
  await dispatcher.page.keyboard.press('Tab'); assert.equal(await dispatcher.page.locator('.skip-link').evaluate(x=>x===document.activeElement),true);
  await dispatcher.page.keyboard.press('Enter');
  await dispatcher.page.emulateMedia({reducedMotion:'reduce'});
  await dispatcher.page.setViewportSize({width:390,height:844});
  await dispatcher.page.evaluate(()=>document.documentElement.style.fontSize='200%');
  assert.equal(await dispatcher.page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),true,'200% text zoom overflow');
  fs.writeFileSync(path.join(output,'accessibility-results.json'),JSON.stringify(scans,null,2));
  assert.equal(scans.filter(s=>s.violations.length).length,0,'Accessibility violations remain; inspect accessibility-results.json');
  console.log(`Lifecycle actions and ${scans.length} role/viewport/theme accessibility scans passed`);
 } finally { if(browser) await browser.close(); server.kill(); await new Promise(r=>server.exitCode!==null?r():server.once('exit',r)); }
})().catch(error=>{console.error(error.message);process.exitCode=1;});
