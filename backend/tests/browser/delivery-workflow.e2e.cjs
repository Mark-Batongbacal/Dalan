const fs=require('node:fs'),path=require('node:path'),http=require('node:http'),assert=require('node:assert/strict');
const {spawn}=require('node:child_process');
const {chromium}=require(process.env.DALAN_PLAYWRIGHT_MODULE || 'playwright');
(async()=>{
 const fixture=JSON.parse(fs.readFileSync(0,'utf8'));
 const output=process.env.DALAN_WORKFLOW_OUTPUT || path.join(require('node:os').tmpdir(),'dalan-guided-flow');fs.mkdirSync(output,{recursive:true});
 const provider=http.createServer(async(req,res)=>{
  let raw='';for await(const chunk of req)raw+=chunk;const body=JSON.parse(raw);res.setHeader('Content-Type','application/json');
  if(req.url.includes('sources_to_targets'))res.end(JSON.stringify({sources_to_targets:{durations:body.sources.map(a=>body.targets.map(b=>a.lat===b.lat&&a.lon===b.lon?0:120)),distances:body.sources.map(a=>body.targets.map(b=>a.lat===b.lat&&a.lon===b.lon?0:1))}}));
  else res.end(JSON.stringify({trip:{summary:{length:1,time:120},legs:[{shape:'_ibE_seK_seK_seK'}]}}));
 });
 await new Promise(resolve=>provider.listen(0,'127.0.0.1',resolve));
 const root=path.resolve(__dirname,'../../src/Dalan.Web'),base='http://127.0.0.1:5201';
 const server=spawn('dotnet',[path.join(root,'bin/Debug/net9.0/Dalan.Web.dll'),'--urls',base],{cwd:root,stdio:'ignore',env:{...process.env,ASPNETCORE_ENVIRONMENT:'Testing',DALAN_ADMIN_PASSWORD:'',ConnectionStrings__Dalan:fixture.connection,Valhalla__BaseUrl:`http://127.0.0.1:${provider.address().port}/`}});
 let browser;const scans=[];
 try{
  let ready=false;for(let i=0;i<100;i++){try{ready=(await fetch(base+'/Account/Login')).ok;}catch{}if(ready)break;await new Promise(r=>setTimeout(r,200));}assert.ok(ready);
  browser=await chromium.launch({channel:'msedge',headless:true});
  async function context(){const c=await browser.newContext({viewport:{width:1440,height:1000},reducedMotion:'reduce'});await c.route('**/tiles.openfreemap.org/**',r=>r.fulfill({json:{version:8,sources:{},layers:[]}}));return c;}
  const c=await context(),page=await c.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
  async function login(p){await p.goto(base+'/Account/Login');await p.locator('#Email').fill(fixture.email);await p.locator('#Password').fill(fixture.password);await Promise.all([p.waitForURL(/\/(Vehicles|DispatchBoard)$/),p.getByRole('button',{name:'Sign in to workspace',exact:true}).click()]);}
  async function submit(name){await Promise.all([page.waitForNavigation(),page.getByRole('button',{name,exact:true}).click()]);}
  await page.goto(base+'/');
  const hero=await page.locator('.landing-signin').boundingBox();assert.ok(Math.abs(hero.x+hero.width/2-720)<3,'Landing panel is centered');await page.screenshot({path:path.join(output,'landing-desktop.png')});
  await page.goto(base+'/Account/Login');assert.notEqual(await page.locator('#toggle-password').evaluate(x=>getComputedStyle(x).borderTopColor),'rgba(0, 0, 0, 0)');await page.screenshot({path:path.join(output,'login-desktop.png')});
  await login(page);await page.goto(base+'/DeliveryWorkflow');assert.equal(await page.locator('.delivery-steps li').count(),6);
  await page.goto(base+'/Warehouse');await page.locator('.maplibregl-canvas').waitFor();
  const map=page.locator('[data-planning-map]'),canvas=page.locator('.maplibregl-canvas');await canvas.hover();
  const before=Number(await map.getAttribute('data-map-zoom'));await page.mouse.wheel(0,-150);await page.waitForTimeout(300);assert.equal(Number(await map.getAttribute('data-map-zoom')),before,'Unlocked wheel leaves zoom unchanged');
  await canvas.click();assert.equal(await map.getAttribute('data-map-locked'),'true');await page.mouse.wheel(0,-350);
  await page.waitForFunction(start=>Number(document.querySelector('[data-planning-map]').dataset.mapZoom)>start,before);
  await page.keyboard.press('Escape');assert.equal(await map.getAttribute('data-map-locked'),'false');
  await canvas.click({position:{x:50,y:100}});await page.getByRole('heading',{name:'Set your warehouse',exact:true}).click();assert.equal(await map.getAttribute('data-map-locked'),'false');
  await page.locator('#Name').fill('Persistent workflow depot');await page.locator('[data-map-use-center]').click();await page.locator('[data-map-confirm]').click();await submit('Save warehouse & continue');assert.match(page.url(),/DeliveryWorkflow/);
  const other=await context(),otherPage=await other.newPage();await login(otherPage);await otherPage.goto(base+'/Deliveries/Create');await otherPage.waitForFunction(()=>document.getElementById('PickupAddress').value==='Persistent workflow depot');await other.close();
  await page.goto(base+'/Deliveries/Create');await page.waitForFunction(()=>document.getElementById('PickupAddress').value==='Persistent workflow depot');
  await page.goto(base+'/DeliveryBatch');await page.locator('#Rows_0__RecipientName').fill('Guided browser One');await page.locator('#Rows_0__DropoffAddress').fill('Destination One');await page.locator('#Rows_0__PayloadWeightKg').fill('2');await page.locator('[data-map-use-center]').click();await page.locator('[data-map-confirm]').click();
  await page.locator('[data-batch-add]').click();assert.equal(await page.locator('[data-batch-row]').count(),2);assert.equal(await page.locator('[data-map-target] option').count(),2);assert.equal(await page.locator('[data-map-target]').inputValue(),'1');
  await page.locator('#Rows_1__RecipientName').fill('Guided browser Two');await page.locator('#Rows_1__DropoffAddress').fill('Destination Two');await page.locator('#Rows_1__PayloadWeightKg').fill('3');await page.locator('[data-map-target]').selectOption('1');await page.locator('[data-map-use-center]').click();await page.locator('[data-map-confirm]').click();await submit('Create deliveries & assign');assert.match(page.url(),/Assignments/);
  const assignment=page.locator('section').filter({has:page.getByRole('heading',{name:'Guided browser One',exact:true})});await assignment.locator('[name=driverId]').selectOption(fixture.driverId);await assignment.locator('[name=vehicleId]').selectOption(fixture.vehicleId);await Promise.all([page.waitForNavigation(),assignment.getByRole('button',{name:'Save assignment',exact:true}).click()]);assert.match(page.url(),/Assignments/);
  const axe=fs.readFileSync(process.env.DALAN_AXE_MODULE,'utf8');
  for(const route of ['/DeliveryWorkflow','/Warehouse','/DeliveryBatch','/DeliveryWorkflow/Assignments','/Deliveries/Create','/DispatchBoard','/FleetTracking'])for(const width of [390,768,1440])for(const theme of ['light','dark','system']){
   await page.setViewportSize({width,height:1000});await page.goto(base+route);await page.locator('#theme-select').selectOption(theme);await page.waitForTimeout(300);
   assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),`${route} ${width} overflow`);await page.addScriptTag({content:axe});
   const violations=await page.evaluate(async()=> (await axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa']}})).violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))})));
   scans.push({route,width,theme,violations});fs.writeFileSync(path.join(output,'accessibility.json'),JSON.stringify(scans,null,2));await page.evaluate(()=>scrollTo(0,0));await page.screenshot({path:path.join(output,`${route.replaceAll('/','_')}-${width}-${theme}.png`),fullPage:true});
  }
  await page.goto(base+'/FleetPlanner');assert.match(page.url(),/DispatchBoard$/);assert.equal(await page.locator('input[name^="Jobs"][type=text]').count(),0);
  await page.waitForFunction(()=>document.getElementById('Settings_WarehouseAddress').value==='Persistent workflow depot');
  await page.locator(`input[name$=".Id"][value="${fixture.vehicleId}"]`).first().evaluate(input=>{const check=input.parentElement.querySelector('[type=checkbox]');if(check){check.checked=true;check.dispatchEvent(new Event('change',{bubbles:true}));}});
  // Locate the bound vehicle checkbox without depending on its registration display text.
  const vehicleId=page.locator(`input[name$=".Id"][value="${fixture.vehicleId}"]`).first();const prefix=(await vehicleId.getAttribute('name')).replace('.Id','');await page.locator('.dispatch-vehicles').evaluate(x=>x.open=true);await page.locator(`[name="${prefix}.Selected"][type=checkbox]`).check();
  await page.getByRole('checkbox',{name:/Guided browser One/}).check();await page.getByRole('checkbox',{name:/Guided browser Two/}).check();await submit('Preview routes');await submit('Review this plan');await submit('Confirm dispatch');await page.getByRole('heading',{name:'Confirmed dispatch',exact:true}).waitFor();
  await page.goto(base+'/FleetTracking');assert.ok(await page.getByRole('heading',{level:1}).isVisible());assert.deepEqual(errors,[]);assert.equal(scans.filter(x=>x.violations.length).length,0,'Inspect accessibility.json');
  console.log(`Passed guided warehouse → fleet → batch → assignment → dispatch → tracking; ${scans.length} UI scans and map focus/zoom checks.`);
 }finally{if(browser)await browser.close();server.kill();provider.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
