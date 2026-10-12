import {chromium} from 'playwright';
import {PNG} from 'pngjs';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile,mkdir,writeFile} from 'node:fs/promises';
import {resolve,extname,sep} from 'node:path';

const root=resolve(process.env.SITE_ROOT??'../../artifacts/site');
const output=resolve(process.env.AXIS_TEST_OUTPUT??'../../artifacts/axis-ticks-results');await mkdir(output,{recursive:true});
const mime={'.html':'text/html','.js':'text/javascript','.json':'application/json','.wasm':'application/wasm','.css':'text/css','.woff2':'font/woff2'};
const server=createServer(async(req,res)=>{try{
 let path=decodeURIComponent(new URL(req.url,'http://localhost').pathname);if(path.endsWith('/'))path+='index.html';
 const file=resolve(root,'.'+path);if(!file.startsWith(root+sep)){res.writeHead(403).end();return;}
 const bytes=await readFile(file);res.writeHead(200,{'Content-Type':mime[extname(file)]??'application/octet-stream','Cache-Control':'no-store'}).end(bytes);
}catch{res.writeHead(404).end();}});
await new Promise(done=>server.listen(0,'127.0.0.1',done));
const browser=await chromium.launch({headless:true,args:['--use-angle=swiftshader','--enable-unsafe-swiftshader','--enable-webgl','--disable-dev-shm-usage']});
const results=[],errors=[],logs=[];let page;
const inspect=()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.Inspect()));
const ticks=()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.InspectAxisTicks()));
const saved=()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.ExportDocument()));
const frames=()=>page.evaluate(()=>new Promise(done=>requestAnimationFrame(()=>requestAnimationFrame(done))));
async function waitRendered(before){await page.waitForFunction(p=>{const s=JSON.parse(globalThis.chartBridge.Inspect());return !s.error&&s.vertices>0&&(s.backend.startsWith('OpenGL')?s.gpuSubmissions>p.gpuSubmissions:s.overlayDraws>p.overlayDraws);},before,{timeout:45000});}
async function actionable(read,label){
 let previous=null,since=0,last;const deadline=Date.now()+10000;
 while(Date.now()<deadline){await frames();last=await read();const v=page.viewportSize();
  const visible=last&&last.width>1&&last.height>1&&last.x>=0&&last.y>=0&&last.x+last.width<=v.width+1&&last.y+last.height<=v.height+1;
  const same=visible&&previous&&['x','y','width','height'].every(k=>Math.abs(last[k]-previous[k])<.1);
  if(!same)since=Date.now();if(same&&Date.now()-since>=200)return last;previous=visible?last:null;
 }
 throw new Error(label+' is not actionable: '+JSON.stringify(last));
}
async function scroll(name){
 let previous=null,since=0,nextWheel=0,last;const deadline=Date.now()+20000;
 while(Date.now()<deadline){await frames();last=await page.evaluate(n=>({b:JSON.parse(globalThis.chartBridge.TickControlBounds(n)),v:JSON.parse(globalThis.chartBridge.TickViewportBounds(n))}),name);
  const {b,v}=last,w=page.viewportSize();const visible=b.width>1&&b.height>1&&b.y>=v.y&&b.y+b.height<=v.y+v.height+1&&b.x>=0&&b.x+b.width<=w.width+1;
  const same=visible&&previous&&['x','y','width','height'].every(k=>Math.abs(b[k]-previous[k])<.1);
  if(!same)since=Date.now();if(same&&Date.now()-since>=200)return b;previous=visible?b:null;
  if(!visible&&Date.now()>=nextWheel){await page.mouse.move(v.x+3,v.y+v.height/2);await page.mouse.wheel(0,Math.max(-550,Math.min(550,b.y+b.height/2-v.y-v.height/2)));nextWheel=Date.now()+250;}
 }
 throw new Error('Tick control is outside its viewport: '+JSON.stringify({name,...last}));
}
async function click(name,header=false){const b=await scroll(name);await page.mouse.click(b.x+b.width/2,header?b.y+b.height-15:b.y+b.height/2);}
async function input(name,value){
 await click(name,true);await page.waitForFunction(()=>['INPUT','TEXTAREA'].includes(document.activeElement?.tagName));
 await page.keyboard.press('Control+a');const lines=value.split('\n');
 for(let i=0;i<lines.length;i++){if(i)await page.keyboard.press('Enter');await page.keyboard.insertText(lines[i]);}
 await page.keyboard.press('Tab');
 await page.waitForFunction(([name,value])=>globalThis.chartBridge.TickControlText(name).replace(/\r\n|\r/g,'\n')===value,[name,value]);
}
async function option(name,index){
 await click(name,true);const b=await actionable(()=>page.evaluate(([n,i])=>{const b=globalThis.chartBridge.TickOptionBounds(n,i);return b?JSON.parse(b):null;},[name,index]),'Tick option');
 await page.mouse.click(b.x+b.width/2,b.y+b.height/2);
 await page.waitForFunction(([n,i])=>globalThis.chartBridge.TickOptionBounds(n,i)==='',[name,index]);
}
async function toolbar(title){
 await page.evaluate(t=>globalThis.chartBridge.BringActionIntoView(t),title);
 const b=await actionable(()=>page.evaluate(t=>JSON.parse(globalThis.chartBridge.ActionBounds(t)),title),title);
 await page.mouse.click(b.x+b.width/2,b.y+b.height/2);
}
async function download(title,path){const pending=page.waitForEvent('download');await toolbar(title);const file=await pending;await file.saveAs(path);return readFile(path);}
async function labelBand(){const s=await inspect();return PNG.sync.read(await page.screenshot({scale:'css',clip:{x:Math.ceil(s.bounds.x+s.plot.x),y:Math.ceil(s.bounds.y+s.plot.y+s.plot.height)+5,width:Math.floor(s.plot.width),height:24}}));}
async function scenario(name,run){try{const detail=await run();results.push({name,passed:true,detail});console.log('PASS '+name);}catch(error){results.push({name,passed:false,error:String(error)});await page.screenshot({path:resolve(output,'failure.png')}).catch(()=>{});throw error;}finally{await writeFile(resolve(output,'results.json'),JSON.stringify({results,errors},null,2));}}
try{
 for(const backend of ['Auto','Skia'])for(const dpr of [1,2]){
  const prefix=backend+' DPR '+dpr;const context=await browser.newContext({viewport:{width:1440,height:960},deviceScaleFactor:dpr,acceptDownloads:true});page=await context.newPage();
  page.on('pageerror',e=>errors.push(e.message));page.on('console',m=>logs.push({type:m.type(),text:m.text()}));
  await page.goto(`http://127.0.0.1:${server.address().port}/FastChartsScene/`,{waitUntil:'domcontentloaded',timeout:180000});
  await page.waitForFunction(()=>globalThis.Module?.getAssemblyExports,undefined,{timeout:180000});
  await page.evaluate(async()=>{const e=await globalThis.Module.getAssemblyExports('FastChartsScene.Explorer');globalThis.chartBridge=e.FastChartsScene.Explorer.BrowserBridge;});
  await page.waitForFunction(()=>{try{return JSON.parse(globalThis.chartBridge.InspectGallery()).open;}catch{return false;}},undefined,{timeout:180000});
  await page.locator('.uno-loader').waitFor({state:'detached',timeout:180000});
  const open=await actionable(()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.ActionBounds('Open example'))),'Open example');
  await page.mouse.click(open.x+open.width/2,open.y+open.height/2);await page.waitForFunction(()=>!JSON.parse(globalThis.chartBridge.InspectGallery()).open);
  await page.evaluate(b=>globalThis.chartBridge.Command('backend',b),backend);
  await page.waitForFunction(b=>{const s=JSON.parse(globalThis.chartBridge.Inspect());return !s.error&&s.vertices>0&&(b==='Skia'?s.backend==='Skia (explicit)':s.backend.startsWith('OpenGL'));},backend,{timeout:180000});
  let customDocument,customOptions;
  await scenario(prefix+': actual custom tick editing changes rendered label pixels',async()=>{
   const beforeBand=await labelBand();await click('$expander');await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectAxisTicks()).expanded);
   await option('Mode',1);await input('Numeric format','F2');await input('Custom ticks','0|Start <&>\n2.5|Quarter\n5|Midpoint\n7.5\n10|End');
   const before=await inspect();await click('Apply ticks');await page.waitForFunction(()=>!JSON.parse(globalThis.chartBridge.InspectAxisTicks()).hasDraft);await waitRendered(before);
   customDocument=await saved();customOptions=customDocument.xAxes[0].tickOptions;
   assert.equal(customOptions.mode,'Custom');assert.equal(customOptions.ticks.length,5);assert.equal(customOptions.ticks[3].label,null);
   const state=await ticks();for(const label of ['Start <&>','Quarter','Midpoint','7.50','End'])assert(state.labels.includes(label),label);
   const after=await labelBand();assert(!beforeBand.data.equals(after.data),'Custom labels did not change actual axis pixels');
   await page.screenshot({path:resolve(output,backend+'-'+dpr+'-custom-ticks.png'),scale:'css'});return {ticks:5,labels:state.labels.filter(s=>['Start <&>','Quarter','Midpoint','7.50','End'].includes(s))};
  });
  await scenario(prefix+': unrelated updates preserve invalid drafts and cached label text',async()=>{
   await input('Custom ticks','2|Duplicate A\n2|Duplicate B');const before=await ticks();
   await page.evaluate(()=>globalThis.chartBridge.CheckTickHostUpdates());await frames();const after=await ticks();
   assert(after.hasDraft);assert.equal(after.textConversions,before.textConversions);assert.equal(after.tickGenerations,before.tickGenerations);
   assert.equal((await page.evaluate(()=>globalThis.chartBridge.TickControlText('Custom ticks'))).replace(/\r\n|\r/g,'\n'),'2|Duplicate A\n2|Duplicate B');
   await click('Apply ticks');await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectAxisTicks()).error.length>0);
   assert.deepEqual((await saved()).xAxes[0].tickOptions,customOptions);assert.equal((await inspect()).error,null);
   await click('Reset tick drafts');await page.waitForFunction(()=>!JSON.parse(globalThis.chartBridge.InspectAxisTicks()).hasDraft&&JSON.parse(globalThis.chartBridge.InspectAxisTicks()).error==='');
  });
  await scenario(prefix+': axis selection discards the old target draft',async()=>{
   await input('Numeric format','Q');await option('$axis',1);
   await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectAxisTicks()).axisId==='y');
   assert.equal(await page.evaluate(()=>globalThis.chartBridge.TickControlText('Numeric format')),'');assert(!(await ticks()).hasDraft);
   assert.deepEqual((await saved()).xAxes[0].tickOptions,customOptions);
   await input('Numeric format','F2');const before=await inspect();await click('Apply ticks');await waitRendered(before);
   assert.equal((await saved()).yAxes[0].tickOptions.numericFormat,'F2');assert((await ticks()).labels.includes('0.00'));
  });
  await scenario(prefix+': saved charts SVG and PNG include edited custom labels',async()=>{
   const json=JSON.parse((await download('Save chart',resolve(output,backend+'-'+dpr+'-chart.json'))).toString());assert.deepEqual(json.xAxes[0].tickOptions,customOptions);
   const svg=(await download('SVG',resolve(output,backend+'-'+dpr+'-chart.svg'))).toString();assert(svg.includes('Start &lt;&amp;&gt;'));assert(svg.includes('7.50'));
   const png=PNG.sync.read(await download('PNG',resolve(output,backend+'-'+dpr+'-chart.png')));assert(png.width>1000&&png.height>1000);
   const before=await inspect();await page.evaluate(s=>globalThis.chartBridge.ImportDocument(JSON.stringify(s)),customDocument);await waitRendered(before);
   assert.deepEqual((await saved()).xAxes[0].tickOptions,customOptions);assert((await ticks()).labels.includes('Midpoint'));
  });
  await scenario(prefix+': secondary inverted logarithmic ticks retain exact positions',async()=>{
   const d=structuredClone(customDocument);d.showLegend=false;
   d.xAxes.push({...d.xAxes[0],id:'custom-x',minimum:1,maximum:100,scale:'Logarithmic',inverted:true,tickOptions:{mode:'Custom',numericFormat:'',dateTimeFormat:'',ticks:[{value:1,label:'one'},{value:10,label:'ten'},{value:100,label:'hundred'}]}});
   for(const series of d.series)series.xAxis='custom-x';
   const before=await inspect();await page.evaluate(d=>globalThis.chartBridge.ImportDocument(JSON.stringify(d)),d);await waitRendered(before);
   const svg=(await download('SVG',resolve(output,backend+'-'+dpr+'-log.svg'))).toString();assert(svg.includes('>one</text>')&&svg.includes('>ten</text>')&&svg.includes('>hundred</text>'));
   const s=await inspect(),match=svg.match(/<text[^>]*x="([^"]+)"[^>]*>ten<\/text>/);assert(match,'Missing log midpoint label');
   assert(Math.abs(Number(match[1])-(s.plot.x+s.plot.width/2))<.01,'Log midpoint must use the bound axis');
  });
  await scenario(prefix+': reusable editor avoids unchanged writes and detaches on unload',async()=>{
   assert.equal(await page.evaluate(()=>globalThis.chartBridge.CheckTickEditorLifetime()),'passed');
  });
  await context.close();
 }
 assert.equal(errors.length,0,errors.join('\n'));
}catch(error){process.exitCode=1;console.error(error.stack??error);if(!results.some(r=>!r.passed))results.push({name:'initialization',passed:false,error:String(error)});}
finally{
 await writeFile(resolve(output,'results.json'),JSON.stringify({passed:results.filter(r=>r.passed).length,total:results.length,softwareAdapter:true,results,errors},null,2));
 await writeFile(resolve(output,'console.json'),JSON.stringify(logs,null,2));console.log(`RESULT ${results.filter(r=>r.passed).length}/${results.length} axis tick scenarios passed`);
 await browser.close();await new Promise(done=>server.close(done));
}
