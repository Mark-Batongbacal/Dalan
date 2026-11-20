const fs=require('node:fs'),path=require('node:path'),http=require('node:http'),assert=require('node:assert/strict');
const {spawn}=require('node:child_process');
const {chromium}=require(process.env.DALAN_PLAYWRIGHT_MODULE || 'playwright');
(async()=>{
 const fixture=JSON.parse(fs.readFileSync(0,'utf8'));
 const output=process.env.DALAN_WORKFLOW_OUTPUT || path.join(require('node:os').tmpdir(),'dalan-guided-flow');fs.mkdirSync(output,{recursive:true});
 function encode(points){let lastLat=0,lastLon=0,out='';for(const p of points){const lat=Math.round(p.lat*1e6),lon=Math.round(p.lon*1e6);for(const delta of [lat-lastLat,lon-lastLon]){let v=delta<0?~(delta<<1):delta<<1;while(v>=32){out+=String.fromCharCode((32|(v&31))+63);v>>>=5;}out+=String.fromCharCode(v+63);}lastLat=lat;lastLon=lon;}return out;}
 const provider=http.createServer(async(req,res)=>{
  let raw='';for await(const chunk of req)raw+=chunk;const body=JSON.parse(raw);
  if(provider.fail){res.statusCode=503;res.end('{}');return;}res.setHeader('Content-Type','application/json');
  if(req.url.includes('sources_to_targets'))res.end(JSON.stringify({sources_to_targets:{durations:body.sources.map(a=>body.targets.map(b=>a.lat===b.lat&&a.lon===b.lon?0:120)),distances:body.sources.map(a=>body.targets.map(b=>a.lat===b.lat&&a.lon===b.lon?0:1))}}));
  else res.end(JSON.stringify({trip:{summary:{length:1,time:120},legs:[{shape:encode(body.locations)}]}}));
 });
 await new Promise(resolve=>provider.listen(0,'127.0.0.1',resolve));
 const root=path.resolve(__dirname,'../../src/Dalan.Web'),base='http://127.0.0.1:5201';
 const server=spawn('dotnet',[path.join(root,'bin/Debug/net9.0/Dalan.Web.dll'),'--urls',base],{cwd:root,stdio:'ignore',env:{...process.env,ASPNETCORE_ENVIRONMENT:'Testing',DALAN_ADMIN_PASSWORD:'',ConnectionStrings__Dalan:fixture.connection,Valhalla__BaseUrl:`http://127.0.0.1:${provider.address().port}/`}});
 let browser;const scans=[];
 try{
  let ready=false;for(let i=0;i<100;i++){try{ready=(await fetch(base+'/Account/Login')).ok;}catch{}if(ready)break;await new Promise(r=>setTimeout(r,200));}assert.ok(ready);
  browser=await chromium.launch({channel:'msedge',headless:true});
  async function context(){const c=await browser.newContext({viewport:{width:1440,height:1000},reducedMotion:'reduce'});if(process.env.DALAN_REAL_BASEMAP!=='1')await c.route('**/tiles.openfreemap.org/**',r=>r.fulfill({json:{version:8,sources:{},layers:[]}}));return c;}
  const c=await context(),page=await c.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
  async function login(p){await p.goto(base+'/Account/Login');await p.locator('#Email').fill(fixture.email);await p.locator('#Password').fill(fixture.password);await Promise.all([p.waitForURL(/\/(Vehicles|DispatchBoard)$/),p.getByRole('button',{name:'Sign in to workspace',exact:true}).click()]);}
  async function submit(name){await Promise.all([page.waitForNavigation(),page.getByRole('button',{name,exact:true}).click()]);}
  await page.goto(base+'/');
  const hero=await page.locator('.landing-signin').boundingBox();assert.ok(Math.abs(hero.x+hero.width/2-720)<3,'Landing panel is centered');await page.screenshot({path:path.join(output,'landing-desktop.png')});
  await page.goto(base+'/Account/Login');assert.notEqual(await page.locator('#toggle-password').evaluate(x=>getComputedStyle(x).borderTopColor),'rgba(0, 0, 0, 0)');await page.screenshot({path:path.join(output,'login-desktop.png')});
  await login(page);await page.goto(base+'/DispatchBoard');assert.equal(await page.locator('[data-assignment-card]').count(),0);
  for(const theme of ['light','dark']){await page.locator('#theme-select').selectOption(theme);await page.waitForTimeout(250);await page.screenshot({path:path.join(output,`empty-${theme}.png`),fullPage:true});}
  await page.goto(base+'/DeliveryWorkflow');assert.equal(await page.locator('.delivery-steps li').count(),6);
  await page.goto(base+'/Warehouse');await page.locator('.maplibregl-canvas').waitFor();
  const map=page.locator('[data-planning-map]'),canvas=page.locator('.maplibregl-canvas');await canvas.hover();
  const before=Number(await map.getAttribute('data-map-zoom'));await page.mouse.wheel(0,-150);await page.waitForTimeout(300);assert.equal(Number(await map.getAttribute('data-map-zoom')),before,'Unlocked wheel leaves zoom unchanged');
  await canvas.click();assert.equal(await map.getAttribute('data-map-locked'),'true');await page.mouse.wheel(0,-350);
  await page.waitForFunction(start=>Number(document.querySelector('[data-planning-map]').dataset.mapZoom)>start,before);
  await page.keyboard.press('Escape');assert.equal(await map.getAttribute('data-map-locked'),'false');
  await canvas.click({position:{x:50,y:100}});await page.getByRole('heading',{name:'Set your warehouse',exact:true}).click();assert.equal(await map.getAttribute('data-map-locked'),'false');
  await page.locator('#Name').fill('Persistent workflow depot');await page.locator('[data-map-use-center]').click();await page.locator('[data-map-confirm]').click();await submit('Save warehouse & continue');assert.match(page.url(),/DeliveryWorkflow/);
  // Populate a long queue through the existing CSRF-protected batch form in this disposable database.
  for(const count of [15,3]) {
   const response=await page.request.get(base+'/DeliveryBatch');const html=await response.text();const token=html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)[1];
   const fields={__RequestVerificationToken:token};for(let i=0;i<count;i++){Object.assign(fields,{[`Rows[${i}].RecipientName`]:`Workspace queue ${count}-${i+1}`,[`Rows[${i}].DropoffAddress`]:`Destination ${i+1}`,[`Rows[${i}].DropoffLatitude`]:String(14.61+i*.001),[`Rows[${i}].DropoffLongitude`]:'121.04'});}
   assert.ok((await page.request.post(base+'/DeliveryBatch',{form:fields})).ok());
  }
  const other=await context(),otherPage=await other.newPage();await login(otherPage);await otherPage.goto(base+'/Deliveries/Create');await otherPage.waitForFunction(()=>document.getElementById('PickupAddress').value==='Persistent workflow depot');await other.close();
  await page.goto(base+'/Deliveries/Create');await page.waitForFunction(()=>document.getElementById('PickupAddress').value==='Persistent workflow depot');
  await page.goto(base+'/DeliveryBatch');await page.locator('#Rows_0__RecipientName').fill('Guided browser One');await page.locator('#Rows_0__DropoffAddress').fill('Destination One');await page.locator('#Rows_0__PayloadWeightKg').fill('2');await page.locator('.maplibregl-canvas').click({position:{x:200,y:180}});await page.locator('[data-map-confirm]').click();
  await page.locator('[data-batch-add]').click();assert.equal(await page.locator('[data-batch-row]').count(),2);assert.equal(await page.locator('[data-map-target] option').count(),2);assert.equal(await page.locator('[data-map-target]').inputValue(),'1');
  await page.locator('#Rows_1__RecipientName').fill('Guided browser Two');await page.locator('#Rows_1__DropoffAddress').fill('Destination Two');await page.locator('#Rows_1__PayloadWeightKg').fill('3');await page.locator('[data-map-target]').selectOption('1');await page.locator('.maplibregl-canvas').click({position:{x:380,y:260}});await page.locator('[data-map-confirm]').click();await submit('Create deliveries & assign');assert.match(page.url(),/Assignments/);
  const assignment=page.locator('section').filter({has:page.getByRole('heading',{name:'Guided browser One',exact:true})});await assignment.locator('[name=driverId]').selectOption(fixture.driverId);await assignment.locator('[name=vehicleId]').selectOption(fixture.vehicleId);await Promise.all([page.waitForNavigation(),assignment.getByRole('button',{name:'Save assignment',exact:true}).click()]);assert.match(page.url(),/Assignments/);
  const axe=fs.readFileSync(process.env.DALAN_AXE_MODULE,'utf8');
  const themeChecks=[];
  async function verifyTheme() {
   const actual=await page.evaluate(async()=>{
    await document.fonts.load('400 15px Inter');
    await document.fonts.load('600 14px Inter');
    const style=getComputedStyle(document.documentElement);
    const dark=document.documentElement.dataset.theme==='dark'||(document.documentElement.dataset.theme==='system'&&matchMedia('(prefers-color-scheme: dark)').matches);
    const fonts=[...document.querySelectorAll('body,h1,.btn,.warehouse-switch,.form-control,.form-select,.maplibregl-map,.maplibregl-ctrl')].map(x=>getComputedStyle(x).fontFamily);
    const typography=selector=>{const element=document.querySelector(selector);if(!element)return null;const s=getComputedStyle(element);return {family:s.fontFamily,size:s.fontSize,weight:s.fontWeight,lineHeight:s.lineHeight};};
    const warehouse=typography('.warehouse-switch'),action=typography('[data-preview-submit]');
    const control=document.querySelector('.maplibregl-ctrl-group');
    const probe=document.createElement('span');probe.style.color='var(--card)';document.body.append(probe);const card=getComputedStyle(probe).color;probe.remove();
    return {dark,fonts,warehouse,action,loaded:[...document.fonts].some(f=>f.family==='Inter'&&f.status==='loaded'),canvas:style.getPropertyValue('--canvas').trim(),primary:style.getPropertyValue('--primary').trim(),nav:style.getPropertyValue('--nav').trim(),card,control:control?getComputedStyle(control).backgroundColor:null};
   });
   assert.ok(actual.loaded,'The actual local Inter font loads');
   assert.ok(actual.fonts.every(font=>font.includes('Inter')),'MVC fields, buttons and map controls use Inter');
   if(actual.warehouse)assert.deepEqual(actual.warehouse,actual.action,'Warehouse selector typography matches the other header action');
   assert.equal(actual.canvas,actual.dark?'#17191D':'#FFFAF6');
   assert.equal(actual.primary,actual.dark?'#E97A2E':'#B94E12');
   assert.equal(actual.nav,actual.dark?'#1A1D21':'#292D32');
   if(actual.control)assert.equal(actual.control,actual.card,'Map controls use the same themed card surface');
   themeChecks.push(actual);fs.writeFileSync(path.join(output,'theme-consistency.json'),JSON.stringify(themeChecks,null,2));
  }
  for(const route of ['/DeliveryWorkflow','/Warehouse','/DeliveryBatch','/DeliveryWorkflow/Assignments','/Deliveries/Create','/DispatchBoard','/FleetTracking'])for(const width of [390,768,1440])for(const theme of ['light','dark','system']){
   await page.setViewportSize({width,height:1000});await page.goto(base+route);await page.locator('#theme-select').selectOption(theme);await page.waitForTimeout(300);
   await verifyTheme();assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),`${route} ${width} overflow`);await page.addScriptTag({content:axe});
   const violations=await page.evaluate(async()=> (await axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa']}})).violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))})));
   scans.push({route,width,theme,violations});fs.writeFileSync(path.join(output,'accessibility.json'),JSON.stringify(scans,null,2));await page.evaluate(()=>scrollTo(0,0));await page.screenshot({path:path.join(output,`${route.replaceAll('/','_')}-${width}-${theme}.png`),fullPage:true});
  }
  await page.goto(base+'/FleetPlanner');assert.match(page.url(),/DispatchBoard$/);assert.equal(await page.locator('input[name^="Jobs"][type=text]').count(),0);
  await page.waitForFunction(()=>document.getElementById('Settings_WarehouseAddress').value==='Persistent workflow depot');
  await page.locator(`input[name$=".Id"][value="${fixture.vehicleId}"]`).first().evaluate(input=>{const check=input.parentElement.querySelector('[type=checkbox]');if(check){check.checked=true;check.dispatchEvent(new Event('change',{bubbles:true}));}});
  // Locate the bound vehicle checkbox without depending on its registration display text.
  const vehicleId=page.locator(`input[name$=".Id"][value="${fixture.vehicleId}"]`).first();const prefix=(await vehicleId.getAttribute('name')).replace('.Id','');await page.locator('.dispatch-vehicles').evaluate(x=>x.open=true);await page.locator(`[name="${prefix}.Selected"][type=checkbox]`).check();
  await page.locator('.dispatch-options').evaluate(x=>x.open=true);
  await page.getByRole('button',{name:'S: Warehouse',exact:true}).waitFor();
  async function capture(stage) {
   for(const width of [390,768,1440]) for(const theme of ['light','dark','system']) {
    await page.setViewportSize({width,height:1000});await page.emulateMedia({colorScheme:theme==='system'?'dark':theme});await page.locator('#theme-select').selectOption(theme);await page.waitForTimeout(250);
    await verifyTheme();await page.addScriptTag({content:axe});const violations=await page.evaluate(async()=> (await axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa']}})).violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))})));
    assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),`${stage} ${width} overflow`);
    scans.push({stage,width,theme,violations});fs.writeFileSync(path.join(output,'accessibility.json'),JSON.stringify(scans,null,2));
    await page.evaluate(()=>scrollTo(0,0));await page.screenshot({path:path.join(output,`${stage}-${width}-${theme}.png`),fullPage:true});
    if(await page.locator('button[data-workspace-view="map"]').isVisible()) {
      await page.locator('button[data-workspace-view="map"]').click();await page.waitForTimeout(150);
      const retry=await page.locator('[data-map-retry]').boundingBox(),tools=await page.locator('.planning-map-tools').boundingBox();assert.ok(tools.y>=retry.y+retry.height,'Map tools do not cover wrapped header controls');
      await page.screenshot({path:path.join(output,`${stage}-map-${width}-${theme}.png`),fullPage:true});
      await page.locator('button[data-workspace-view="deliveries"]').click();
    }
   }
   await page.setViewportSize({width:1440,height:1000});
  }
  assert.ok(await page.locator('[data-assignment-card]').count()>=20);
  await capture('prepare-long-queue');
  // Enforce selection caps without silently dropping checked items, including across filtering.
  const jobs=page.locator('input[type=checkbox][name^="Deliveries["]');
  for(let i=0;i<15;i++) await jobs.nth(i).check();assert.ok(await jobs.nth(15).isDisabled());
  await page.locator('[data-assignment-search]').fill('not-a-recipient');assert.equal(await page.locator('[data-assignment-card]:visible').count(),0);
  await page.locator('[data-assignment-search]').fill('');for(let i=0;i<15;i++)await jobs.nth(i).uncheck();assert.ok(await jobs.nth(15).isEnabled());
  const fleet=page.locator('input[type=checkbox][name^="Vehicles["]');
  await page.locator('.dispatch-vehicles').evaluate(x=>x.open=true);for(let i=0;i<8;i++)await fleet.nth(i).check();assert.ok(await fleet.nth(8).isDisabled());for(let i=0;i<8;i++)await fleet.nth(i).uncheck();
  await submit('Preview routes');assert.match(await page.locator('[data-dispatch-error]').innerText(),/Select 1/);await capture('selection-error');
  await page.locator('.dispatch-vehicles').evaluate(x=>x.open=true);
  await page.locator(`[name="${prefix}.Selected"][type=checkbox]`).check();
  await page.getByRole('checkbox',{name:/Guided browser One/}).check();await page.getByRole('checkbox',{name:/Guided browser Two/}).check();
  await page.locator('#Strategy').selectOption('WorkloadEquity');await page.locator('.dispatch-options').evaluate(x=>x.open=true);await page.locator('#Settings_ServiceSecondsPerStop').fill('60');
  provider.fail=true;await submit('Preview routes');assert.match(await page.locator('[data-dispatch-error]').innerText(),/routing provider failed/i);await capture('provider-error');provider.fail=false;
  // Exercise the real submit handler without navigating away while capturing its transient progress UI.
  await page.evaluate(()=>{const form=document.getElementById('dispatch-preparation');form.dispatchEvent(new SubmitEvent('submit',{bubbles:true,cancelable:true,submitter:document.querySelector('[data-preview-submit]')}));});
  await page.locator('[data-preview-progress]:visible').waitFor();assert.ok(await page.locator('[data-preview-submit]').isDisabled());await page.screenshot({path:path.join(output,'preview-loading.png'),fullPage:true});
  await page.evaluate(()=>{document.querySelector('[data-preview-submit]').disabled=false;document.getElementById('dispatch-preparation').removeAttribute('aria-busy');});await submit('Preview routes');await page.getByRole('heading',{name:'Route preview',exact:true}).waitFor();
  assert.equal(await page.locator('#Strategy').inputValue(),'WorkloadEquity');assert.equal(await page.locator('#Settings_ServiceSecondsPerStop').inputValue(),'60');
  await page.locator('[data-focus-route]').first().click();await page.waitForFunction(()=>document.querySelector('[data-map-status]').textContent.includes('Road route loaded'));
  assert.equal(await page.locator('[data-map-route]').inputValue(),fixture.vehicleId);await capture('preview');
  await page.locator('.dispatch-options').evaluate(x=>x.open=true);await page.locator('#Settings_ServiceSecondsPerStop').fill('61');assert.ok(await page.locator('[data-review-preview]').isDisabled());assert.match(await page.locator('[data-preview-validity]').innerText(),/Preview routes again/);
  await submit('Preview routes');assert.ok(await page.locator('[data-review-preview]').isEnabled());
  await submit('Review this plan');await capture('review');
  await submit('Back to selections (new preview required)');assert.equal(await page.locator('#Strategy').inputValue(),'WorkloadEquity');assert.ok(await page.getByRole('checkbox',{name:/Guided browser One/}).isChecked());
  await submit('Preview routes');await submit('Review this plan');
  // Reverse the delivery-pair order while retaining pickup before drop-off and all bound indices.
  await page.evaluate(()=>{const rows=[...document.querySelectorAll('tbody tr')];const groups=new Map();for(const row of rows){const id=row.querySelector('input[name$=".DeliveryId"]').value;const list=groups.get(id)||[];list.push(row);groups.set(id,list);}let order=0;for(const pair of [...groups.values()].reverse()){pair.sort((a,b)=>a.querySelector('input[name$=".Kind"]').value==='Pickup'?-1:1);for(const row of pair){const input=row.querySelector('input[name$=".Order"]');input.value=order++;input.dispatchEvent(new Event('input',{bubbles:true}));}}});
  await submit('Confirm dispatch');await page.getByRole('heading',{name:'Confirmed dispatch',exact:true}).waitFor();await capture('confirmed');
  await page.goto(base+'/FleetTracking');assert.ok(await page.getByRole('heading',{level:1}).isVisible());
  await page.goto(base+'/');assert.match(page.url(),/DispatchBoard$/);assert.equal(await page.locator('.delivery-steps').count(),0);assert.deepEqual(errors,[]);assert.equal(scans.filter(x=>x.violations.length).length,0,'Inspect accessibility.json');
  console.log(`Passed unified workspace limits, search, provider errors, preview geometry, editable review, confirmation and responsive themes; ${scans.length} scans.`);
 }finally{if(browser)await browser.close();server.kill();provider.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
