import assert from 'node:assert/strict';
import {resolve} from 'node:path';
import {readFile} from 'node:fs/promises';
import {execFileSync} from 'node:child_process';
import {PNG} from 'pngjs';

export async function validateGallery(page,{inspect,scenario,output}){
 const state=()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.InspectGallery()));
 const frames=()=>page.evaluate(()=>new Promise(done=>requestAnimationFrame(()=>requestAnimationFrame(done))));
 async function stableBounds(read,label){
  const deadline=Date.now()+10000;let previous=null,stableSince=0,last;
  while(Date.now()<deadline){
   await frames();last=await read();
   const viewport=page.viewportSize();
   const visible=last&&last.width>1&&last.height>1&&last.x>=0&&last.y>=0&&last.x+last.width<=viewport.width+1&&last.y+last.height<=viewport.height+1;
   const same=visible&&previous&&['x','y','width','height'].every(k=>Math.abs(last[k]-previous[k])<.1);
   if(!same)stableSince=Date.now();
   if(same&&Date.now()-stableSince>=200)return last;
   previous=visible?last:null;
  }
  throw new Error(label+' did not reach stable visible bounds: '+JSON.stringify(last));
 }
 async function click(name){
  await page.evaluate(n=>globalThis.chartBridge.BringGalleryControlIntoView(n),name);
  const b=await stableBounds(()=>page.evaluate(n=>JSON.parse(globalThis.chartBridge.GalleryControlBounds(n)),name),name);
  await page.mouse.click(b.x+b.width/2,b.y+b.height/2);
 }
 async function toolbar(title){
  const b=await stableBounds(()=>page.evaluate(t=>JSON.parse(globalThis.chartBridge.ActionBounds(t)),title),title);
  await page.mouse.click(b.x+b.width/2,b.y+b.height/2);
 }
 async function option(name,value){
  await click(name);await page.waitForFunction(n=>globalThis.chartBridge.GalleryDropDownIsOpen(n),name);
  const b=await stableBounds(()=>page.evaluate(([n,v])=>{const text=globalThis.chartBridge.GalleryOptionBounds(n,v);return text?JSON.parse(text):null;},[name,value]),name+' option '+value);
  await page.mouse.click(b.x+b.width/2,b.y+b.height/2);await page.waitForFunction(n=>!globalThis.chartBridge.GalleryDropDownIsOpen(n),name);
 }
 async function paintedGallery(){
  // Visibility changes precede the Skia composition pass. Require the actual opaque
  // root pixels, not a fixed delay or a managed flag, before capturing gallery evidence.
  const deadline=Date.now()+10000;
  while(Date.now()<deadline){
   await frames();const png=PNG.sync.read(await page.screenshot({scale:'css'}));
   const at=(200*png.width+5)*4;
   if(png.data[at]===48&&png.data[at+1]===48&&png.data[at+2]===48)return;
   await page.waitForTimeout(50);
  }
  throw new Error('Gallery root was not painted opaque charcoal over the chart');
 }
 async function query(text){
  const before=await state();
  await click('Search examples');await page.waitForFunction(()=>['INPUT','TEXTAREA'].includes(document.activeElement?.tagName));
  await page.keyboard.press('Control+a');await page.keyboard.insertText(text);await page.keyboard.press('Tab');
  await page.waitForFunction(([text,previous])=>{const s=JSON.parse(globalThis.chartBridge.InspectGallery());return s.query===text&&s.filterPasses>previous;},[text,before.filterPasses],{timeout:10000});
 }
 async function entry(id){
  await page.evaluate(id=>globalThis.chartBridge.BringGalleryEntryIntoView(id),id);
  const b=await stableBounds(()=>page.evaluate(id=>{const text=globalThis.chartBridge.GalleryEntryBounds(id);return text?JSON.parse(text):null;},id),'entry '+id);
  await page.mouse.click(b.x+b.width/2,b.y+b.height-15);await page.waitForFunction(id=>JSON.parse(globalThis.chartBridge.InspectGallery()).selected===id,id);
 }
 async function home(){await toolbar('Back to gallery');await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectGallery()).open);await paintedGallery();}
 await scenario('gallery: reference-style tiles display real cached chart previews',async()=>{
  await home();await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectGallery()).generatedPreviews>=3,undefined,{timeout:90000});
  const s=await state();assert.equal(s.visible.length,31);assert(s.cachedPreviews<=32);assert(s.pendingPreviews<=64);assert.equal(s.previewErrors,0);
  await page.screenshot({path:resolve(output,'gallery-tiles.png'),scale:'css'});return s;
 });
 await scenario('gallery: keyword AND search and quoted phrases filter the catalog',async()=>{
  await query('scalar logarithmic');assert.deepEqual((await state()).visible,['color-scales']);
  await query('"One million samples"');assert.deepEqual((await state()).visible,['million']);
  await query('not-an-example');assert.equal((await state()).visible.length,0);
  await page.screenshot({path:resolve(output,'gallery-empty.png'),scale:'css'});
  await click('Clear search');await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectGallery()).visible.length===31);
 });
 await scenario('gallery: tag toggles filter without rebuilding chart documents',async()=>{
  const before=(await inspect()).revision;
  await click('Tag 3D');await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectGallery()).visible.length===7);
  assert((await state()).visible.includes('surface'));await click('Tag FFT');await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectGallery()).visible.length===0);
  await option('Tag matching','Match any');
  await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectGallery()).visible.length===9);
  assert.equal((await inspect()).revision,before);await click('Clear search');
 });
 await scenario('gallery: category popup filters with real pointer selection',async()=>{
  await option('Example category','Financial');await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectGallery()).visible.length===2);
  assert.deepEqual((await state()).visible,['financial','ohlc']);await click('Clear search');
  await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectGallery()).visible.length===31);
 });
 await scenario('gallery: list view preserves selection and opens using the keyboard',async()=>{
  await click('List view');await entry('surface');assert.equal((await state()).tiles,false);
  await page.screenshot({path:resolve(output,'gallery-list.png'),scale:'css'});
  await page.keyboard.press('Enter');await page.waitForFunction(()=>!JSON.parse(globalThis.chartBridge.InspectGallery()).open&&JSON.parse(globalThis.chartBridge.Inspect()).example==='surface');
  await home();assert.equal((await state()).tiles,false);assert.equal((await state()).selected,'surface');
 });
 await scenario('gallery: favorites toggle filter and actual download preserve exact IDs',async()=>{
  await click('Favorite selected example');assert((await state()).favorites.includes('surface'));
  await entry('signals');await click('Favorite selected example');await click('Favorites only');
  await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectGallery()).visible.length===2);
  assert.deepEqual((await state()).visible,['signals','surface']);
  const pending=page.waitForEvent('download');await click('Export favorites');const file=await pending;const path=resolve(output,'gallery-favorites.json');await file.saveAs(path);
  const saved=JSON.parse((await readFile(path)).toString());assert.equal(saved.format,'FastChartsScene.Favorites');assert.deepEqual(saved.ids,['signals','surface']);
 });
 await scenario('gallery: malformed favorite imports are atomic and valid imports refresh filters',async()=>{
  const before=await page.evaluate(()=>globalThis.chartBridge.ExportFavorites());
  await assert.rejects(()=>page.evaluate(()=>globalThis.chartBridge.ImportFavorites('{"format":"FastChartsScene.Favorites","version":1,"ids":["missing"]}')));
  assert.equal(await page.evaluate(()=>globalThis.chartBridge.ExportFavorites()),before);
  await page.evaluate(()=>globalThis.chartBridge.ImportFavorites('{"format":"FastChartsScene.Favorites","version":1,"ids":["surface"]}'));
  await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectGallery()).visible.length===1);assert.equal((await state()).selected,'surface');
  await click('Clear search');await click('Tile view');
 });
 await scenario('gallery: back navigation stops acquisition and reuses preview cache',async()=>{
  await entry('signals');await click('Open example');await page.waitForFunction(()=>!JSON.parse(globalThis.chartBridge.InspectGallery()).open);
  await toolbar('Start acquisition');await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.Inspect()).streaming);
  await home();assert.equal((await inspect()).streaming,false);
  const first=await state();await click('Open example');await page.waitForFunction(()=>!JSON.parse(globalThis.chartBridge.InspectGallery()).open);await home();
  const next=await state();assert(next.cachedPreviews<=32);assert(next.pendingPreviews<=64);assert(next.generatedPreviews<=first.generatedPreviews+2);assert.equal(next.previewErrors,0);
 });
 await scenario('gallery: project extraction downloads the current edited standalone chart',async()=>{
  await entry('surface');await click('Open example');await page.waitForFunction(()=>!JSON.parse(globalThis.chartBridge.InspectGallery()).open);
  await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.Inspect()).example==='surface'&&JSON.parse(globalThis.chartBridge.Inspect()).vertices>0);
  await page.evaluate(()=>globalThis.chartBridge.Command('projection','true'));
  const saved=await page.evaluate(()=>globalThis.chartBridge.ExportDocument());
  await page.evaluate(()=>globalThis.chartBridge.BringActionIntoView('Extract project'));await frames();
  const pending=page.waitForEvent('download');await toolbar('Extract project');const file=await pending;const path=resolve(output,'surface-project.zip');await file.saveAs(path);
  const names=execFileSync('unzip',['-Z1',path],{encoding:'utf8'}).trim().split('\n');assert.equal(names.length,12);assert(names.includes('ExtractedChart.csproj'));
  const doc=execFileSync('unzip',['-p',path,'chart.json'],{encoding:'utf8'});assert.equal(doc,saved);
  const project=execFileSync('unzip',['-p',path,'ExtractedChart.csproj'],{encoding:'utf8'});assert(project.includes('net10.0-desktop')&&project.includes('net10.0-browserwasm'));assert(!project.includes('ProjectReference'));
  await home();return {entries:names.length,bytes:(await readFile(path)).length};
 });
 await scenario('gallery: favorites and list preference survive browser reload',async()=>{
  await click('List view');await entry('signals');await click('Open example');await page.waitForFunction(()=>!JSON.parse(globalThis.chartBridge.InspectGallery()).open);
  await page.reload({waitUntil:'domcontentloaded',timeout:180000});
  await page.waitForFunction(()=>globalThis.Module?.getAssemblyExports,undefined,{timeout:180000});
  await page.evaluate(async()=>{const e=await globalThis.Module.getAssemblyExports('FastChartsScene.Explorer');globalThis.chartBridge=e.FastChartsScene.Explorer.BrowserBridge;document.addEventListener('wheel',e=>globalThis.lastChartWheel={x:e.clientX,y:e.clientY},{capture:true,passive:true});});
  await page.waitForFunction(()=>{try{return JSON.parse(globalThis.chartBridge.InspectGallery()).open;}catch{return false;}},undefined,{timeout:180000});
  await page.locator('.uno-loader').waitFor({state:'detached',timeout:180000});
  const s=await state();assert.deepEqual(s.favorites,['surface']);assert.equal(s.tiles,false);
  if(process.env.RENDER_MODE==='Skia')await page.evaluate(()=>globalThis.chartBridge.Command('backend','Skia'));
  await click('Tile view');await entry('signals');await click('Open example');await page.waitForFunction(()=>!JSON.parse(globalThis.chartBridge.InspectGallery()).open);
 });
}
