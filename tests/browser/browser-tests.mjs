import { chromium } from 'playwright';
import { PNG } from 'pngjs';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { resolve, extname, sep } from 'node:path';

const root=resolve(process.env.SITE_ROOT??'../../artifacts/site');
const output=resolve(process.env.TEST_OUTPUT??'../../artifacts/browser-results');
await mkdir(output,{recursive:true});
const mime={'.html':'text/html','.js':'text/javascript','.mjs':'text/javascript','.json':'application/json','.wasm':'application/wasm','.css':'text/css','.woff2':'font/woff2','.png':'image/png','.svg':'image/svg+xml'};
const server=createServer(async(req,res)=>{
  try {
    let pathname=decodeURIComponent(new URL(req.url,'http://localhost').pathname);
    if(pathname.endsWith('/')) pathname+='index.html';
    const file=resolve(root,'.'+pathname);
    if(!file.startsWith(root+sep)) { res.writeHead(403).end(); return; }
    const bytes=await readFile(file); res.writeHead(200,{'Content-Type':mime[extname(file)]??'application/octet-stream','Cache-Control':'no-store'}); res.end(bytes);
  } catch { res.writeHead(404).end(); }
});
await new Promise(done=>server.listen(0,'127.0.0.1',done));
const url=process.env.TEST_URL??`http://127.0.0.1:${server.address().port}/FastChartsScene/?test=1`;
const results=[]; const consoleLog=[]; const pageErrors=[];
const browser=await chromium.launch({headless:true,args:['--use-angle=swiftshader','--enable-unsafe-swiftshader','--enable-webgl','--disable-dev-shm-usage']});
let page;
const inspect=()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.Inspect()));
const command=(name,value='')=>page.evaluate(([n,v])=>globalThis.chartBridge.Command(n,v),[name,String(value)]);
async function waitFor(predicate,label,timeout=30000) {
  const deadline=Date.now()+timeout; let last;
  while(Date.now()<deadline) {
    last=await inspect();
    if(last.error) throw new Error(`${label}: ${last.error}`);
    if(predicate(last)) return last;
    await page.waitForTimeout(50);
  }
  throw new Error(`${label} timed out: ${JSON.stringify(last)}`);
}
async function scenario(name,run) {
  await run(); const state=await inspect(); results.push({name,passed:true,state}); console.log('PASS '+name);
  await writeFile(resolve(output,'results.json'),JSON.stringify({url,browser:browser.version(),softwareAdapter:true,results},null,2));
}
async function open(dpr) {
  const context=await browser.newContext({viewport:{width:1440,height:960},deviceScaleFactor:dpr,acceptDownloads:true});
  page=await context.newPage();
  page.on('console',m=>consoleLog.push({type:m.type(),text:m.text()}));
  page.on('pageerror',e=>pageErrors.push(e.message));
  await page.goto(url,{waitUntil:'domcontentloaded',timeout:180000});
  await page.waitForFunction(()=>globalThis.Module?.getAssemblyExports,undefined,{timeout:180000});
  await page.evaluate(async()=>{
    const exports=await globalThis.Module.getAssemblyExports('FastChartsScene.Explorer');
    globalThis.chartBridge=exports.FastChartsScene.Explorer.BrowserBridge;
  });
  // Assembly exports become available before Uno calls Application.OnLaunched.
  // Only this startup gate tolerates the specific not-yet-created managed page.
  await page.waitForFunction(()=>{
    try { const state=JSON.parse(globalThis.chartBridge.Inspect()); return state.bounds.width>100; }
    catch(error) { if(String(error).includes('Explorer not initialized')) return false; throw error; }
  },undefined,{timeout:180000});
  await waitFor(s=>s.vertices>0&&s.gpuSubmissions>0&&s.backend.startsWith('OpenGL'),'GPU presentation',180000);
  return context;
}
async function select(id) {
  const before=await inspect(); await command('example',id);
  return waitFor(s=>s.example===id&&s.vertices>0&&s.gpuSubmissions>before.gpuSubmissions,`render ${id}`,90000);
}
async function image(name,state=undefined) {
  state??=await inspect();
  const bytes=await page.screenshot({path:resolve(output,name+'.png'),scale:'css'});
  const png=PNG.sync.read(bytes); const p=state.plot; const b=state.bounds;
  let colored=0;
  for(let y=Math.max(0,Math.ceil(b.y+p.y+3));y<Math.min(png.height,Math.floor(b.y+p.y+p.height-3));y++)
    for(let x=Math.max(0,Math.ceil(b.x+p.x+3));x<Math.min(png.width,Math.floor(b.x+p.x+p.width-3));x++) {
      if(x>b.x+p.x+p.width-185&&y<b.y+p.y+100) continue;
      const at=(y*png.width+x)*4; const r=png.data[at],g=png.data[at+1],blue=png.data[at+2];
      if(Math.max(r,g,blue)-Math.min(r,g,blue)>45&&Math.max(r,g,blue)>75) colored++;
    }
  assert(colored>100,`${name}: chart plot contains only ${colored} colored pixels`);
  return colored;
}
function center(s,ux=.35,uy=.5) { return {x:s.bounds.x+s.plot.x+s.plot.width*ux,y:s.bounds.y+s.plot.y+s.plot.height*uy}; }
function xRange(s) { return s.axes.find(a=>a.id==='x'); }
const span=r=>r.maximum-r.minimum;
async function clickAction(title) {
  const bounds=await page.evaluate(t=>JSON.parse(globalThis.chartBridge.ActionBounds(t)),title);
  await page.mouse.click(bounds.x+bounds.width/2,bounds.y+bounds.height/2);
}
try {
  let context=await open(1);
  await scenario('initial GPU frame is visibly rendered',async()=>{ await image('initial'); });
  const examples=(await inspect()).examples;
  for(const id of examples) await scenario('GPU example: '+id,async()=>{ const state=await select(id); await image('example-'+id,state); });
  await scenario('actual pointer wheel preserves data anchor',async()=>{
    const before=await select('signals'); const point=center(before,.3,.55);
    await page.mouse.click(point.x,point.y); await page.mouse.wheel(0,-120);
    const after=await waitFor(s=>span(xRange(s))<span(xRange(before)),'wheel zoom');
    assert(Math.abs((xRange(before).minimum+.3*span(xRange(before)))-(xRange(after).minimum+.3*span(xRange(after))))<1e-5);
  });
  await scenario('keyboard undo and redo restore viewport',async()=>{
    const zoomed=await inspect(); await page.keyboard.press('z');
    await waitFor(s=>Math.abs(span(xRange(s))-10)<1e-8,'keyboard undo');
    await page.keyboard.press('y'); await waitFor(s=>Math.abs(span(xRange(s))-span(xRange(zoomed)))<1e-8,'keyboard redo');
  });
  await scenario('pointer drag pans and rectangle gesture zooms',async()=>{
    const before=await select('signals'); const point=center(before);
    await page.mouse.move(point.x,point.y); await page.mouse.down(); await page.mouse.move(point.x+65,point.y+20,{steps:8}); await page.mouse.up();
    await waitFor(s=>Math.abs(xRange(s).minimum)>0.1,'drag pan');
    const state=await inspect(); const a=center(state,.2,.25); const b=center(state,.65,.75);
    await page.keyboard.down('Shift'); await page.mouse.move(a.x,a.y); await page.mouse.down(); await page.mouse.move(b.x,b.y,{steps:8}); await page.mouse.up(); await page.keyboard.up('Shift');
    await waitFor(s=>span(xRange(s))<span(xRange(state))*.6,'rectangle zoom');
  });
  await scenario('legend pointer toggles real geometry',async()=>{
    const before=await select('signals'); const b=before.bounds,p=before.plot;
    await page.mouse.click(b.x+p.x+p.width-125,b.y+p.y+18);
    const hidden=await waitFor(s=>s.vertices<before.vertices,'hide legend series');
    await page.mouse.click(b.x+p.x+p.width-125,b.y+p.y+18);
    await waitFor(s=>s.vertices>hidden.vertices,'restore legend series');
  });
  await scenario('toolbar starts and pauses bounded streaming data',async()=>{
    await select('streaming'); await clickAction('Start acquisition');
    const active=await waitFor(s=>s.streaming&&xRange(s).maximum>10.2,'streaming acquisition'); assert(active.sourcePoints<=60000);
    await clickAction('Pause acquisition'); const paused=await waitFor(s=>!s.streaming,'pause acquisition');
    await page.waitForTimeout(250); assert.equal(xRange(await inspect()).maximum,xRange(paused).maximum);
  });
  for(const theme of ['light','contrast','dark']) await scenario('theme: '+theme,async()=>{
    const before=await inspect(); await command('theme',theme); await waitFor(s=>s.gpuSubmissions>before.gpuSubmissions,'theme redraw'); await image('theme-'+theme);
  });
  await scenario('Skia fallback produces 2D pixels',async()=>{
    await select('signals'); const before=await inspect(); await command('backend','Skia');
    await waitFor(s=>s.backend.startsWith('Skia')&&s.overlayDraws>before.overlayDraws,'Skia presentation'); await image('skia-2d');
    await command('backend','Auto'); await waitFor(s=>s.backend.startsWith('OpenGL'),'restore OpenGL');
  });
  await scenario('PNG and CSV toolbar downloads contain actual data',async()=>{
    await select('signals');
    let promise=page.waitForEvent('download'); await clickAction('PNG'); let download=await promise;
    const pngFile=resolve(output,'export.png'); await download.saveAs(pngFile); const png=PNG.sync.read(await readFile(pngFile)); assert(png.width>500&&png.height>300);
    promise=page.waitForEvent('download'); await clickAction('CSV'); download=await promise;
    const csvFile=resolve(output,'export.csv'); await download.saveAs(csvFile); const csv=await readFile(csvFile,'utf8'); assert(csv.startsWith('X,Y\n')); assert(csv.split('\n').length>10000);
  });
  await scenario('projection and orbit update the 3D image',async()=>{
    await select('surface'); const before=await inspect(); await command('projection',true);
    await waitFor(s=>s.orthographic&&s.gpuSubmissions>before.gpuSubmissions,'orthographic projection');
    const point=center(await inspect()); await page.mouse.move(point.x,point.y); await page.mouse.down(); await page.mouse.move(point.x+75,point.y+25,{steps:6}); await page.mouse.up(); await image('3d-orbit');
  });
  await context.close(); context=await open(2);
  for(const id of ['signals','heatmap','surface']) await scenario('DPR 2: '+id,async()=>{ await select(id); await image('dpr2-'+id); });
  await scenario('narrow viewport preserves usable chart layout',async()=>{
    await select('signals'); await page.setViewportSize({width:760,height:820}); await waitFor(s=>s.bounds.width>300&&s.bounds.width<760,'tablet layout'); await image('tablet');
    await page.setViewportSize({width:480,height:800}); await waitFor(s=>s.bounds.width>350&&s.bounds.width<=480,'mobile layout'); await image('mobile');
  });
  assert.equal(pageErrors.length,0,'Unhandled page errors: '+pageErrors.join('\n'));
  console.log(`RESULT ${results.length}/${results.length} browser scenarios passed (software WebGL adapter; no hardware performance claim)`);
  await context.close();
} catch(error) {
  process.exitCode=1; console.error(error.stack??error);
  results.push({name:'failure',passed:false,error:String(error)});
  if(page&&!page.isClosed()) { await page.screenshot({path:resolve(output,'failure.png')}).catch(()=>{}); console.error('STATE',await inspect().catch(e=>String(e))); }
} finally {
  await writeFile(resolve(output,'results.json'),JSON.stringify({url,browser:browser.version(),softwareAdapter:true,results,pageErrors},null,2));
  await writeFile(resolve(output,'console.json'),JSON.stringify(consoleLog,null,2));
  console.log('LAST CONSOLE',JSON.stringify(consoleLog.slice(-20)));
  await browser.close(); await new Promise(done=>server.close(done));
}
