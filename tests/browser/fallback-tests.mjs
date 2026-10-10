// Separate 2D Skia coverage. This never substitutes for the mandatory GPU suite.
import { chromium } from 'playwright';
import { PNG } from 'pngjs';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { resolve, extname, sep } from 'node:path';

const root=resolve(process.env.SITE_ROOT??'../../artifacts/site');
const output=resolve(process.env.TEST_OUTPUT??'../../artifacts/fallback-results');
await mkdir(output,{recursive:true});
const mime={'.html':'text/html','.js':'text/javascript','.mjs':'text/javascript','.json':'application/json','.wasm':'application/wasm','.css':'text/css','.woff2':'font/woff2','.png':'image/png','.svg':'image/svg+xml'};
const server=createServer(async(req,res)=>{
  try {
    let pathname=decodeURIComponent(new URL(req.url,'http://localhost').pathname);
    if(pathname.endsWith('/')) pathname+='index.html';
    const file=resolve(root,'.'+pathname);
    if(!file.startsWith(root+sep)) { res.writeHead(403).end(); return; }
    const bytes=await readFile(file);
    res.writeHead(200,{'Content-Type':mime[extname(file)]??'application/octet-stream','Cache-Control':'no-store'}); res.end(bytes);
  } catch { if(!res.headersSent) res.writeHead(404); res.end(); }
});
await new Promise(done=>server.listen(0,'127.0.0.1',done));
const url=process.env.TEST_URL??`http://127.0.0.1:${server.address().port}/FastChartsScene/?fallback-test=1`;
const results=[],consoleLog=[],pageErrors=[];
const browser=await chromium.launch({headless:true,args:['--use-angle=swiftshader','--enable-unsafe-swiftshader','--enable-webgl','--disable-dev-shm-usage']});
let page;
const inspect=()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.Inspect()));
const command=(name,value='')=>page.evaluate(([n,v])=>globalThis.chartBridge.Command(n,v),[name,String(value)]);
async function waitFor(predicate,label,timeout=20000) {
  const deadline=Date.now()+timeout; let state;
  while(Date.now()<deadline) {
    state=await inspect(); if(state.error) throw new Error(`${label}: ${state.error}`);
    if(predicate(state)) return state;
    await page.waitForTimeout(50);
  }
  throw new Error(`${label} timed out: ${JSON.stringify(state)}`);
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
    document.addEventListener('wheel',e=>{ globalThis.lastChartWheel={x:e.clientX,y:e.clientY}; },{capture:true,passive:true});
  });
  await page.waitForFunction(()=>{
    try { return JSON.parse(globalThis.chartBridge.Inspect()).bounds.width>100; }
    catch(e) { if(String(e).includes('Explorer not initialized')) return false; throw e; }
  },undefined,{timeout:180000});
  // Managed layout and SKCanvas drawing can occur while Uno's HTML splash still covers the window.
  // Wait for its real removal rather than accepting a managed counter as proof of presentation.
  await page.locator('.uno-loader').waitFor({state:'detached',timeout:180000});
  await command('backend','Skia');
  await waitFor(s=>s.backend==='Skia (explicit)'&&s.vertices>0&&s.overlayDraws>0,'explicit Skia presentation');
  return context;
}
async function select(id) {
  await command('stream',false); const before=await inspect(); await command('example',id);
  return waitFor(s=>s.example===id&&s.vertices>0&&s.overlayDraws>before.overlayDraws&&s.backend==='Skia (explicit)',`Skia render ${id}`,90000);
}
const axis=s=>s.axes.find(a=>a.id==='x');
const span=r=>r.maximum-r.minimum;
function point(s,x=.35,y=.5) { return {x:s.bounds.x+s.plot.x+s.plot.width*x,y:s.bounds.y+s.plot.y+s.plot.height*y}; }
async function click(title) {
  const b=await page.evaluate(t=>JSON.parse(globalThis.chartBridge.ActionBounds(t)),title);
  await page.mouse.click(b.x+b.width/2,b.y+b.height/2);
}
async function picture(name) {
  await page.evaluate(()=>new Promise(done=>requestAnimationFrame(()=>requestAnimationFrame(done))));
  const state=await inspect(); const b=state.bounds,p=state.plot;
  const png=PNG.sync.read(await page.screenshot({path:resolve(output,name+'.png'),scale:'css'}));
  let colored=0;
  for(let y=Math.max(0,Math.ceil(b.y+p.y+3));y<Math.min(png.height,Math.floor(b.y+p.y+p.height-3));y++)
    for(let x=Math.max(0,Math.ceil(b.x+p.x+3));x<Math.min(png.width,Math.floor(b.x+p.x+p.width-3));x++) {
      if(x>b.x+p.x+p.width-185&&y<b.y+p.y+100) continue;
      const at=(y*png.width+x)*4; const c=[png.data[at],png.data[at+1],png.data[at+2]];
      if(Math.max(...c)-Math.min(...c)>45&&Math.max(...c)>75) colored++;
    }
  assert(colored>100,`${name}: only ${colored} colored plot pixels (legend excluded)`);
  return colored;
}
async function scenario(name,action) {
  try {
    const detail=await action(); const state=await inspect(); results.push({name,passed:true,detail,state}); console.log('PASS '+name);
  } catch(e) {
    results.push({name,passed:false,error:String(e)}); console.error('FAIL '+name+': '+e.stack);
    await page.screenshot({path:resolve(output,'failure-'+results.length+'.png')}).catch(()=>{});
  }
  await writeFile(resolve(output,'results.json'),JSON.stringify({suite:'Skia 2D only',gpuValidated:false,results},null,2));
}
try {
  let context=await open(1);
  const excluded=new Set(['surface','waterfall','bars3d','helix','mesh','pie','donut']);
  const examples=(await inspect()).examples.filter(id=>!excluded.has(id));
  assert.equal(examples.length,22);
  for(const id of examples) await scenario('2D pixels: '+id,async()=>{ await select(id); return {coloredPixels:await picture('skia-'+id)}; });
  await scenario('pointer wheel and keyboard history',async()=>{
    const before=await select('signals'); const p=point(before,.3,.55);
    await page.mouse.click(p.x,p.y); await page.evaluate(()=>{globalThis.lastChartWheel=null;}); await page.mouse.wheel(0,-120);
    const after=await waitFor(s=>span(axis(s))<10,'wheel zoom');
    const wheel=await page.evaluate(()=>globalThis.lastChartWheel); assert(wheel,'Browser did not dispatch a wheel event.');
    // Browser WheelEvent coordinates may be rounded independently of the requested mouse position.
    // Test the data anchor at the coordinates actually delivered to the app, not an idealized 30% point.
    const u=(wheel.x-before.bounds.x-before.plot.x)/before.plot.width;
    const anchorBefore=axis(before).minimum+u*span(axis(before));
    const anchorAfter=axis(after).minimum+u*span(axis(after));
    assert(Math.abs(anchorBefore-anchorAfter)<1e-7,JSON.stringify({wheel,u,anchorBefore,anchorAfter,before:axis(before),after:axis(after)}));
    await page.keyboard.press('z'); await waitFor(s=>Math.abs(span(axis(s))-10)<1e-8,'keyboard undo');
    await page.keyboard.press('y'); await waitFor(s=>Math.abs(span(axis(s))-span(axis(after)))<1e-8,'keyboard redo');
    return {wheel,anchorBefore,anchorAfter};
  });
  await scenario('pointer pan and rectangle zoom',async()=>{
    const before=await select('signals'); const p=point(before);
    await page.mouse.move(p.x,p.y); await page.mouse.down(); await page.mouse.move(p.x+65,p.y+20,{steps:8}); await page.mouse.up();
    await waitFor(s=>Math.abs(axis(s).minimum)>.1,'pointer pan');
    const state=await inspect(),a=point(state,.2,.25),b=point(state,.65,.75);
    await page.keyboard.down('Shift'); await page.mouse.move(a.x,a.y); await page.mouse.down(); await page.mouse.move(b.x,b.y,{steps:8}); await page.mouse.up(); await page.keyboard.up('Shift');
    await waitFor(s=>span(axis(s))<span(axis(state))*.6,'rectangle zoom');
  });
  await scenario('legend pointer toggles geometry',async()=>{
    const before=await select('signals'); const b=before.bounds,p=before.plot;
    const x=b.x+p.x+p.width-125,y=b.y+p.y+18;
    await page.mouse.click(x,y); const hidden=await waitFor(s=>s.vertices<before.vertices,'hide series');
    await page.mouse.click(x,y); await waitFor(s=>s.vertices>hidden.vertices,'show series');
  });
  await scenario('toolbar acquisition start and pause',async()=>{
    try {
      await select('streaming'); await click('Start acquisition');
      const active=await waitFor(s=>s.streaming&&axis(s).maximum>10.2,'acquisition'); assert(active.sourcePoints<=60000);
      await click('Pause acquisition'); const paused=await waitFor(s=>!s.streaming,'pause');
      await page.waitForTimeout(250); assert.equal(axis(await inspect()).maximum,axis(paused).maximum);
    } finally { await command('stream',false); }
  });
  for(const theme of ['light','contrast','dark']) await scenario('theme pixels: '+theme,async()=>{
    await select('signals');
    if(theme==='dark') {
      // Fixtures start dark; assigning the same immutable theme is correctly a no-op.
      const initial=await inspect(); await command('theme','light'); await waitFor(s=>s.overlayDraws>initial.overlayDraws,'prepare dark-theme transition');
    }
    const before=await inspect(); await command('theme',theme);
    await waitFor(s=>s.overlayDraws>before.overlayDraws,'theme redraw'); return {coloredPixels:await picture('theme-'+theme)};
  });
  for(const kind of ['PNG','CSV','Save chart']) await scenario('toolbar download: '+kind,async()=>{
    await select('signals'); const pending=page.waitForEvent('download',{timeout:15000}); await click(kind); const download=await pending;
    const path=resolve(output,download.suggestedFilename()); await download.saveAs(path); const bytes=await readFile(path);
    if(kind==='PNG') { const image=PNG.sync.read(bytes); assert(image.width>500&&image.height>300); }
    if(kind==='CSV') { const text=bytes.toString('utf8'); assert(/^X,Y\r?\n/.test(text)); assert(text.split('\n').length>10000); }
    if(kind==='Save chart') { const document=JSON.parse(bytes.toString('utf8')); assert.equal(document.format,'FastChartsScene'); assert.equal(document.series.length,3); }
    return {file:download.suggestedFilename(),bytes:bytes.length};
  });
  await scenario('managed document import roundtrip preserves pixels and data',async()=>{
    const before=await select('signals');
    const saved=await page.evaluate(()=>globalThis.chartBridge.ExportDocument());
    const previous=await select('band'); await page.evaluate(json=>globalThis.chartBridge.ImportDocument(json),saved);
    const loaded=await waitFor(s=>s.example==='document'&&s.sourcePoints===before.sourcePoints&&s.vertices>0&&s.overlayDraws>previous.overlayDraws,'document import presentation');
    assert.equal(loaded.vertices,before.vertices);
    assert.equal(await page.evaluate(()=>globalThis.chartBridge.ExportDocument()),saved);
    await picture('imported-document');
  });
  await scenario('invalid import preserves the current document',async()=>{
    await select('signals'); const before=await page.evaluate(()=>globalThis.chartBridge.ExportDocument());
    await assert.rejects(()=>page.evaluate(()=>globalThis.chartBridge.ImportDocument('{"format":"wrong"}')));
    assert.equal(await page.evaluate(()=>globalThis.chartBridge.ExportDocument()),before);
  });
  await context.close(); context=await open(2);
  for(const id of ['signals','heatmap']) await scenario('DPR 2 pixels: '+id,async()=>{ await select(id); return {coloredPixels:await picture('dpr2-'+id)}; });
  await scenario('narrow viewport keeps the chart visible',async()=>{
    await select('signals'); await page.setViewportSize({width:760,height:820}); await waitFor(s=>s.bounds.width>300&&s.bounds.width<760,'tablet layout'); await picture('tablet');
    await page.setViewportSize({width:480,height:800}); await waitFor(s=>s.bounds.width>350&&s.bounds.width<=480,'mobile layout'); await picture('mobile');
  });
  await context.close();
} catch(e) {
  results.push({name:'suite initialization or teardown',passed:false,error:String(e)}); console.error(e.stack);
} finally {
  const passed=results.filter(r=>r.passed).length;
  await writeFile(resolve(output,'results.json'),JSON.stringify({suite:'Skia 2D only',gpuValidated:false,browser:browser.version(),passed,total:results.length,results,pageErrors},null,2));
  await writeFile(resolve(output,'console.json'),JSON.stringify(consoleLog,null,2));
  console.log(`RESULT ${passed}/${results.length} Skia 2D scenarios passed; GPU validation remains separate`);
  if(passed!==results.length||pageErrors.length) process.exitCode=1;
  await browser.close(); await new Promise(done=>server.close(done));
}
