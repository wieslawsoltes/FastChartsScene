import { chromium } from 'playwright';
import { PNG } from 'pngjs';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { resolve, extname, sep } from 'node:path';
import { validateGallery } from './gallery-tests.mjs';
import { validateCompactActions } from './responsive-tests.mjs';
import { validateIntensity } from './intensity-tests.mjs';
import { observeChartContexts, validateGpuLifecycle } from './gpu-lifecycle-tests.mjs';

const fallback=process.env.RENDER_MODE==='Skia';
const suite=fallback?'Skia fallback':'WebGL GPU';
const root=resolve(process.env.SITE_ROOT??'../../artifacts/site');
const output=resolve(process.env.TEST_OUTPUT??(fallback?'../../artifacts/fallback-results':'../../artifacts/browser-results'));
await mkdir(output,{recursive:true});
process.env.COMPACT_INSPECTOR_SCREENSHOT=resolve(output,'mobile-inspector.png');
const mime={'.html':'text/html','.js':'text/javascript','.mjs':'text/javascript','.json':'application/json','.wasm':'application/wasm','.css':'text/css','.woff2':'font/woff2','.png':'image/png','.svg':'image/svg+xml'};
const server=createServer(async(req,res)=>{
 try{
  let pathname=decodeURIComponent(new URL(req.url,'http://localhost').pathname);if(pathname.endsWith('/'))pathname+='index.html';
  const file=resolve(root,'.'+pathname);if(!file.startsWith(root+sep)){res.writeHead(403).end();return;}
  const bytes=await readFile(file);res.writeHead(200,{'Content-Type':mime[extname(file)]??'application/octet-stream','Cache-Control':'no-store'});res.end(bytes);
 }catch{if(!res.headersSent)res.writeHead(404);res.end();}
});
await new Promise(done=>server.listen(0,'127.0.0.1',done));
const url=process.env.TEST_URL??`http://127.0.0.1:${server.address().port}/FastChartsScene/?test=1`;
const results=[],consoleLog=[],pageErrors=[];
const browser=await chromium.launch({headless:true,args:['--use-angle=swiftshader','--enable-unsafe-swiftshader','--enable-webgl','--disable-dev-shm-usage']});
let page;
const inspect=()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.Inspect()));
const command=(name,value='')=>page.evaluate(([n,v])=>globalThis.chartBridge.Command(n,v),[name,String(value)]);
const count=s=>fallback?s.overlayDraws:s.gpuSubmissions;
const backend=s=>fallback?s.backend==='Skia (explicit)':s.backend.startsWith('OpenGL');
async function waitFor(predicate,label,timeout=30000){
 const end=Date.now()+timeout;let last;
 while(Date.now()<end){last=await inspect();if(last.error)throw new Error(`${label}: ${last.error}`);if(predicate(last))return last;await page.waitForTimeout(50);}
 throw new Error(`${label} timed out: ${JSON.stringify(last)}`);
}
async function scenario(name,action){
 try{const detail=await action();results.push({name,passed:true,detail,state:await inspect()});console.log('PASS '+name);}
 catch(error){results.push({name,passed:false,error:String(error),state:await inspect().catch(()=>null)});await page.screenshot({path:resolve(output,'failure-'+results.length+'.png')}).catch(()=>{});throw error;}
 finally{await writeFile(resolve(output,'results.json'),JSON.stringify({suite,url,browser:browser.version(),softwareAdapter:true,results},null,2));}
}
async function open(dpr){
 const context=await browser.newContext({viewport:{width:1440,height:960},deviceScaleFactor:dpr,acceptDownloads:true});page=await context.newPage();
 page.on('console',m=>consoleLog.push({type:m.type(),text:m.text()}));page.on('pageerror',e=>pageErrors.push(e.message));
 await observeChartContexts(page);
 await page.goto(url,{waitUntil:'domcontentloaded',timeout:180000});
 await page.waitForFunction(()=>globalThis.Module?.getAssemblyExports,undefined,{timeout:180000});
 await page.evaluate(async()=>{
  const exports=await globalThis.Module.getAssemblyExports('FastChartsScene.Explorer');globalThis.chartBridge=exports.FastChartsScene.Explorer.BrowserBridge;
  document.addEventListener('wheel',e=>{globalThis.lastChartWheel={x:e.clientX,y:e.clientY};},{capture:true,passive:true});
 });
 await page.waitForFunction(()=>{try{return JSON.parse(globalThis.chartBridge.Inspect()).bounds.width>100;}catch(e){if(String(e).includes('Explorer not initialized'))return false;throw e;}},undefined,{timeout:180000});
 await page.locator('.uno-loader').waitFor({state:'detached',timeout:180000});
 await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectGallery()).open);
 await page.screenshot({path:resolve(output,'gallery-start-dpr'+dpr+'.png'),scale:'css'});
 const openBounds=await page.evaluate(()=>JSON.parse(globalThis.chartBridge.ActionBounds('Open example')));
 await page.mouse.click(openBounds.x+openBounds.width/2,openBounds.y+openBounds.height/2);
 await page.waitForFunction(()=>!JSON.parse(globalThis.chartBridge.InspectGallery()).open);
 if(fallback)await command('backend','Skia');
 await waitFor(s=>s.vertices>0&&count(s)>0&&backend(s),suite+' presentation',180000);return context;
}
async function select(id){
 await command('stream',false);const before=await inspect();await command('example',id);
 return waitFor(s=>s.example===id&&s.vertices>0&&count(s)>count(before)&&backend(s),'render '+id,90000);
}
async function image(name){
 await page.evaluate(()=>new Promise(done=>requestAnimationFrame(()=>requestAnimationFrame(done))));
 const state=await inspect();const png=PNG.sync.read(await page.screenshot({path:resolve(output,name+'.png'),scale:'css'}));const p=state.plot,b=state.bounds;
 let colored=0;
 for(let y=Math.max(0,Math.ceil(b.y+p.y+3));y<Math.min(png.height,Math.floor(b.y+p.y+p.height-3));y++)
  for(let x=Math.max(0,Math.ceil(b.x+p.x+3));x<Math.min(png.width,Math.floor(b.x+p.x+p.width-3));x++){
   if(x>b.x+p.x+p.width-185&&y<b.y+p.y+100)continue;
   const i=(y*png.width+x)*4;const r=png.data[i],g=png.data[i+1],blue=png.data[i+2];
   if(Math.max(r,g,blue)-Math.min(r,g,blue)>45&&Math.max(r,g,blue)>75)colored++;
  }
 assert(colored>100,`${name}: only ${colored} colored plot pixels; legend excluded`);return {coloredPixels:colored};
}
function center(s,x=.35,y=.5){return{x:s.bounds.x+s.plot.x+s.plot.width*x,y:s.bounds.y+s.plot.y+s.plot.height*y};}
const axis=s=>s.axes.find(a=>a.id==='x');const span=a=>a.maximum-a.minimum;
async function click(title){const b=await page.evaluate(t=>JSON.parse(globalThis.chartBridge.ActionBounds(t)),title);await page.mouse.click(b.x+b.width/2,b.y+b.height/2);}
async function download(title,name){const pending=page.waitForEvent('download',{timeout:45000});await click(title);const file=await pending;const path=resolve(output,name??file.suggestedFilename());await file.saveAs(path);return readFile(path);}
try{
 let context=await open(1);
 await validateGallery(page,{inspect,scenario,output});
 await waitFor(s=>s.vertices>0&&count(s)>0&&backend(s),'chart after gallery');
 await scenario('initial visible chart',()=>image('initial'));
 const examples=(await inspect()).examples;assert.equal(examples.length,31);
 for(const id of examples)await scenario('example: '+id,async()=>{await select(id);return image('example-'+id);});
 await scenario('pointer wheel preserves delivered data anchor',async()=>{
  const before=await select('signals'),p=center(before,.3,.55);await page.mouse.click(p.x,p.y);await page.evaluate(()=>{globalThis.lastChartWheel=null;});await page.mouse.wheel(0,-120);
  const after=await waitFor(s=>span(axis(s))<10,'wheel zoom');const event=await page.evaluate(()=>globalThis.lastChartWheel);assert(event);
  const u=(event.x-before.bounds.x-before.plot.x)/before.plot.width;
  const a=axis(before).minimum+u*span(axis(before)),b=axis(after).minimum+u*span(axis(after));assert(Math.abs(a-b)<1e-7,JSON.stringify({event,a,b}));return {event,anchorBefore:a,anchorAfter:b};
 });
 await scenario('keyboard undo and redo',async()=>{
  const zoomed=await inspect();await page.keyboard.press('z');await waitFor(s=>Math.abs(span(axis(s))-10)<1e-8,'undo');
  await page.keyboard.press('y');await waitFor(s=>Math.abs(span(axis(s))-span(axis(zoomed)))<1e-8,'redo');
 });
 await scenario('pointer pan and rectangle zoom',async()=>{
  const before=await select('signals'),p=center(before);await page.mouse.move(p.x,p.y);await page.mouse.down();await page.mouse.move(p.x+65,p.y+20,{steps:8});await page.mouse.up();await waitFor(s=>Math.abs(axis(s).minimum)>.1,'pan');
  const state=await inspect(),a=center(state,.2,.25),b=center(state,.65,.75);await page.keyboard.down('Shift');await page.mouse.move(a.x,a.y);await page.mouse.down();await page.mouse.move(b.x,b.y,{steps:8});await page.mouse.up();await page.keyboard.up('Shift');
  await waitFor(s=>span(axis(s))<span(axis(state))*.6,'rectangle zoom');
 });
 await scenario('legend pointer changes real geometry',async()=>{
  const before=await select('signals'),b=before.bounds,p=before.plot;await page.mouse.click(b.x+p.x+p.width-125,b.y+p.y+18);const hidden=await waitFor(s=>s.vertices<before.vertices,'hide series');
  await page.mouse.click(b.x+p.x+p.width-125,b.y+p.y+18);await waitFor(s=>s.vertices>hidden.vertices,'show series');
 });
 await scenario('toolbar acquisition and bounded pause',async()=>{
  try{await select('streaming');await click('Start acquisition');const running=await waitFor(s=>s.streaming&&axis(s).maximum>10.2,'acquisition');assert(running.sourcePoints<=60000);
   await click('Pause acquisition');const paused=await waitFor(s=>!s.streaming,'pause');await page.waitForTimeout(250);assert.equal(axis(await inspect()).maximum,axis(paused).maximum);
  }finally{await command('stream',false);}
 });
 for(const theme of ['light','contrast','dark'])await scenario('theme: '+theme,async()=>{
  await select('signals');if(theme==='dark'){const s=await inspect();await command('theme','light');await waitFor(n=>count(n)>count(s),'prepare theme change');}
  const before=await inspect();await command('theme',theme);await waitFor(s=>count(s)>count(before),'theme redraw');return image('theme-'+theme);
 });
 for(const kind of ['PNG','CSV','SVG','All data CSV','Save chart'])await scenario('toolbar download: '+kind,async()=>{
  await select('signals');const bytes=await download(kind);
  if(kind==='PNG'){const png=PNG.sync.read(bytes);assert(png.width>500&&png.height>300);}
  if(kind==='CSV'){assert(/^X,Y\r?\n/.test(bytes.toString()));assert(bytes.toString().split('\n').length>10000);}
  if(kind==='SVG'){const svg=bytes.toString();assert(svg.startsWith('<svg'));assert(svg.includes('<polygon'));assert(svg.includes('<clipPath'));}
  if(kind==='All data CSV'){const csv=bytes.toString();assert(csv.startsWith('SeriesId,SeriesTitle,Kind'));assert(csv.includes('"Carrier"')&&csv.includes('"Modulated"')&&csv.includes('"Reference"'));assert.equal(csv.split('\r\n').length,36002);}
  if(kind==='Save chart'){const json=JSON.parse(bytes.toString());assert.equal(json.format,'FastChartsScene');assert.equal(json.series.length,3);}
  return {bytes:bytes.length};
 });
 await scenario('document import is exact and invalid import is atomic',async()=>{
  const before=await select('signals');const saved=await page.evaluate(()=>globalThis.chartBridge.ExportDocument());await select('band');const previous=await inspect();
  await page.evaluate(s=>globalThis.chartBridge.ImportDocument(s),saved);const loaded=await waitFor(s=>s.example==='document'&&s.vertices>0&&count(s)>count(previous),'import');
  assert.equal(loaded.vertices,before.vertices);assert.equal(loaded.sourcePoints,before.sourcePoints);assert.equal(await page.evaluate(()=>globalThis.chartBridge.ExportDocument()),saved);await image('imported-document');
  await assert.rejects(()=>page.evaluate(()=>globalThis.chartBridge.ImportDocument('{"format":"wrong"}')));assert.equal(await page.evaluate(()=>globalThis.chartBridge.ExportDocument()),saved);
 });
 await scenario('3D projection orbit and fit',async()=>{
  const before=await select('surface');await command('projection',true);await waitFor(s=>s.orthographic&&count(s)>count(before),'projection');
  const p=center(await inspect());await page.mouse.move(p.x,p.y);await page.mouse.down();await page.mouse.move(p.x+75,p.y+25,{steps:6});await page.mouse.up();await image('3d-orbit');
  await click('Fit');await waitFor(s=>!s.orthographic,'fit camera reset');await image('3d-fit');
 });
 await scenario('3D PNG contains shaded geometry',async()=>{
  await select('surface');const png=PNG.sync.read(await download('PNG','surface-export.png'));let colored=0;
  for(let i=0;i<png.data.length;i+=4)if(Math.max(png.data[i],png.data[i+1],png.data[i+2])-Math.min(png.data[i],png.data[i+1],png.data[i+2])>45)colored++;
  assert(colored>10000,'3D export lacks substantial colored geometry');return {width:png.width,height:png.height,coloredPixels:colored};
 });
 await scenario('financial CSV includes OHLC and overlay series',async()=>{
  await select('financial');const csv=(await download('All data CSV','financial.csv')).toString();assert(csv.includes(',financial,')&&csv.includes(',line,'));assert(csv.includes('Open,High,Low,Close,Volume'));
 });
 if(!fallback)await scenario('switch to 3D software fallback and back to GPU',async()=>{
  await select('surface');const before=await inspect();await command('backend','Skia');await waitFor(s=>s.backend==='Skia (explicit)'&&s.overlayDraws>before.overlayDraws,'3D fallback');await image('3d-fallback');
  await command('backend','Auto');await waitFor(s=>s.backend.startsWith('OpenGL'),'restore GPU');await image('gpu-restored');
 });
 if(!fallback)await validateGpuLifecycle(page,{inspect,select,scenario,image});
 await validateIntensity(page,{inspect,select,scenario,output});
 await context.close();context=await open(2);
 for(const id of ['signals','heatmap','surface'])await scenario('DPR 2: '+id,async()=>{await select(id);return image('dpr2-'+id);});
 const intensityDpr2=resolve(output,'dpr2-intensity');await mkdir(intensityDpr2,{recursive:true});
 await validateIntensity(page,{inspect,select,scenario:(name,run)=>scenario('DPR 2: '+name,run),output:intensityDpr2});
 await scenario('tablet and mobile layout',async()=>{
  await select('signals');await page.setViewportSize({width:760,height:820});await waitFor(s=>s.bounds.width>300&&s.bounds.width<=760,'tablet layout');await image('tablet');
  await page.setViewportSize({width:480,height:800});await waitFor(s=>s.bounds.width>350&&s.bounds.width<=480,'mobile layout');await image('mobile');
 });
 await validateCompactActions(page,inspect,scenario,image);
 assert.equal(pageErrors.length,0,'Unhandled page errors: '+pageErrors.join('\n'));await context.close();
}catch(error){process.exitCode=1;console.error(error.stack??error);if(!results.some(r=>!r.passed))results.push({name:'initialization or teardown',passed:false,error:String(error)});
 if(page&&!page.isClosed()){await page.screenshot({path:resolve(output,'failure.png')}).catch(()=>{});console.error('STATE',await inspect().catch(e=>String(e)));}
}finally{
 const passed=results.filter(r=>r.passed).length;
 await writeFile(resolve(output,'results.json'),JSON.stringify({suite,url,browser:browser.version(),softwareAdapter:true,passed,total:results.length,results,pageErrors},null,2));
 await writeFile(resolve(output,'console.json'),JSON.stringify(consoleLog,null,2));console.log('LAST CONSOLE',JSON.stringify(consoleLog.slice(-30)));
 console.log(`RESULT ${passed}/${results.length} ${suite} scenarios passed (software adapter; no hardware performance claim)`);
 await browser.close();await new Promise(done=>server.close(done));
}
