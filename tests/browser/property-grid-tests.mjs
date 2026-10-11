import assert from 'node:assert/strict';
import {resolve} from 'node:path';

// Diagnostics only find/scroll native controls and inspect values. Edits below use real
// mouse/keyboard input; chart fixture imports and resource lifetime probes are explicit.
export async function validateProperties(page,{inspect,scenario,output}) {
 const state=()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.InspectProperties()));
 const saved=()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.ExportDocument()));
 const frames=()=>page.evaluate(()=>new Promise(done=>requestAnimationFrame(()=>requestAnimationFrame(done))));
 async function bounds(read){
  const end=Date.now()+10000;let last=null,raw=null,since=0;
  while(Date.now()<end){
   await frames();const b=await read(),v=page.viewportSize();raw=b;
   const visible=b&&b.width>1&&b.height>1&&b.x>=0&&b.y>=0&&b.x+b.width<=v.width+1&&b.y+b.height<=v.height+1;
   if(!visible||!last||!['x','y','width','height'].every(k=>Math.abs(b[k]-last[k])<.1))since=Date.now();
   if(visible&&last&&Date.now()-since>=160)return b;last=visible?b:null;
  }
  throw new Error('Property control did not become actionable: '+JSON.stringify(raw));
 }
 async function propertyBounds(category,name){
  // Group expansion and focus may enqueue their own bring-into-view requests. A single
  // programmatic request can be superseded. Scroll with real pointer input and wait for
  // the target to remain inside its actual scrolling viewport, not just the browser.
  const end=Date.now()+15000;let previous=null,stableSince=0,nextWheel=0,last;
  while(Date.now()<end){
   await frames();
   last=await page.evaluate(([c,n])=>({
    target:JSON.parse(globalThis.chartBridge.PropertyBounds(c,n)),
    viewport:JSON.parse(globalThis.chartBridge.PropertyViewportBounds(c,n))
   }),[category,name]);
   const b=last.target,v=last.viewport,w=page.viewportSize();
   const inside=b.width>1&&b.height>1&&b.x>=v.x-1&&b.x+b.width<=v.x+v.width+1&&
    b.y>=v.y-1&&b.y+b.height<=v.y+v.height+1&&b.x>=0&&b.y>=0&&b.x+b.width<=w.width+1&&b.y+b.height<=w.height+1;
   const same=inside&&previous&&['x','y','width','height'].every(k=>Math.abs(b[k]-previous[k])<.1);
   if(!same)stableSince=Date.now();
   if(same&&Date.now()-stableSince>=250)return b;
   previous=inside?b:null;
   if(!inside&&Date.now()>=nextWheel){
    assert(v.width>1&&v.height>1&&v.x>=0&&v.y>=0&&v.x+v.width<=w.width+1&&v.y+v.height<=w.height+1,'Inspector viewport must be visible');
    const delta=Math.max(-600,Math.min(600,b.y+b.height/2-v.y-v.height/2));
    assert(Math.abs(delta)>1,'Property is horizontally clipped, not vertically scrollable');
    await page.mouse.move(v.x+v.width/2,v.y+v.height/2);
    await page.mouse.wheel(0,delta);nextWheel=Date.now()+200;
   }
  }
  throw new Error('Property remained outside its viewport after real wheel scrolling: '+JSON.stringify({category,name,...last}));
 }
 async function click(category,name){
  const b=await propertyBounds(category,name);
  // Checkbox glyph is left-aligned within the value column; clicking its padded right side
  // is not guaranteed to hit the native glyph. Text/numeric editors accept the center.
  const isCheck=await page.evaluate(([c,n])=>['true','false'].includes(globalThis.chartBridge.PropertyText(c,n)),[category,name]).catch(()=>false);
  await page.mouse.click(b.x+(isCheck?12:b.width/2),b.y+b.height/2);return b;
 }
 async function text(category,name,value){
  await click(category,name);
  await page.waitForFunction(()=>['INPUT','TEXTAREA'].includes(document.activeElement?.tagName));
  await page.keyboard.press('Control+a');await page.keyboard.insertText(value);await page.keyboard.press('Tab');
 }
 async function action(title){
  await page.evaluate(t=>globalThis.chartBridge.BringActionIntoView(t),title);
  const b=await bounds(()=>page.evaluate(t=>JSON.parse(globalThis.chartBridge.ActionBounds(t)),title));
  await page.mouse.click(b.x+b.width/2,b.y+b.height/2);
 }
 async function option(category,name,index){
  await click(category,name);
  const b=await bounds(()=>page.evaluate(([c,n,i])=>{const b=globalThis.chartBridge.PropertyOptionBounds(c,n,i);return b?JSON.parse(b):null;},[category,name,index]));
  await page.mouse.click(b.x+b.width/2,b.y+b.height/2);
 }
 async function select(id){
  const before=await inspect();await page.evaluate(id=>globalThis.chartBridge.Command('example',id),id);
  await page.waitForFunction(([id,p])=>{const s=JSON.parse(globalThis.chartBridge.Inspect());return s.example===id&&s.vertices>0&&(s.gpuSubmissions>p.gpuSubmissions||s.overlayDraws>p.overlayDraws);},[id,before]);
 }
 await scenario('properties: compact categorized editors preserve collapse state through search',async()=>{
  await select('signals');const s=await state();assert(s.rows>25&&s.rows<64);assert.equal(s.rows,s.visibleRows);
  await click('Series','$group');await page.waitForFunction(n=>JSON.parse(globalThis.chartBridge.InspectProperties()).visibleRows<n,s.visibleRows);
  const collapsed=(await state()).visibleRows;
  await text('$search','', 'Marker');await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectProperties()).visibleRows===2);
  await page.screenshot({path:resolve(output,'properties-filter.png'),scale:'css'});
  await text('$search','','');await page.waitForFunction(n=>JSON.parse(globalThis.chartBridge.InspectProperties()).visibleRows===n,collapsed);
  await click('Series','$group');await page.waitForFunction(n=>JSON.parse(globalThis.chartBridge.InspectProperties()).visibleRows===n,s.rows);
 });
 await scenario('properties: native title editing updates the rendered and saved document',async()=>{
  const before=await inspect();
  await text('Chart','Title','Measured <signal> "A"');
  await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.ExportDocument()).title==='Measured <signal> "A"');
  await page.waitForFunction(p=>{const s=JSON.parse(globalThis.chartBridge.Inspect());return s.gpuSubmissions>p.gpuSubmissions||s.overlayDraws>p.overlayDraws;},before);
  const title=await page.evaluate(()=>JSON.parse(globalThis.chartBridge.PropertyBounds('Chart','Title')));
  assert(title.width<190&&title.height<50,'Property values should fit compact two-column rows');
  await page.screenshot({path:resolve(output,'properties-editor.png'),scale:'css'});
 });
 await scenario('properties: invalid range drafts are atomic and reset to the actual viewport',async()=>{
  const original=(await saved()).xAxes[0];await text('Axis','Minimum','12');await text('Axis','Maximum','9');
  assert.deepEqual((await saved()).xAxes[0],original);
  await action('Apply range');await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectProperties()).error.length>0);
  assert.deepEqual((await saved()).xAxes[0],original);assert.equal((await inspect()).error,null);
  await action('Reset draft');await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectProperties()).error==='');
  assert.equal(await page.evaluate(()=>globalThis.chartBridge.PropertyText('Axis','Minimum')),String(original.minimum));
  await text('Axis','Minimum','1');await text('Axis','Maximum','8');await action('Apply range');
  await page.waitForFunction(()=>{const x=JSON.parse(globalThis.chartBridge.ExportDocument()).xAxes[0];return x.minimum===1&&x.maximum===8;});
 });
 await scenario('properties: axis inversion and series selection edit the intended object',async()=>{
  await click('Axis','Inverted');await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.ExportDocument()).xAxes[0].inverted);
  await option('Series','Selected series',1);
  await page.waitForFunction(()=>globalThis.chartBridge.PropertyText('Series','Title')==='Modulated');
  await text('Series','Title','Edited second channel');
  await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.ExportDocument()).series[1].title==='Edited second channel');
  const d=await saved();assert.equal(d.series[0].title,'Carrier');assert.equal(d.series[2].title,'Reference');
 });
 await scenario('properties: editor construction remains bounded for 128-series documents',async()=>{
  await select('signals');const d=await saved();d.showLegend=false;d.series=Array.from({length:128},(_,i)=>({...structuredClone(d.series[0]),title:'Channel '+i,data:{kind:'uniform',start:0,step:1,y:[0,1]}}));
  const before=await inspect();await page.evaluate(json=>globalThis.chartBridge.ImportDocument(json),JSON.stringify(d));
  await page.waitForFunction(p=>{const s=JSON.parse(globalThis.chartBridge.Inspect());return s.seriesCount===128&&s.vertices>0&&(s.gpuSubmissions>p.gpuSubmissions||s.overlayDraws>p.overlayDraws);},before);
  const p=await state();assert(p.rows<64);assert.equal((await saved()).series.length,128);return {series:128,editorRows:p.rows};
 });
 await scenario('properties: reusable property batching and gallery unload-after-dispose are safe',async()=>{
  assert.equal(await page.evaluate(()=>globalThis.chartBridge.CheckReusableControlLifetime()),'passed');
 });
 await select('signals');
}
