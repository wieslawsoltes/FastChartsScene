import assert from 'node:assert/strict';
import {PNG} from 'pngjs';

// Runs on the real Uno browser app, under both GPU and fallback suites at DPR 2.
// Imported axis bindings deliberately disagree with the default axes; a stale or ignored
// binding changes the chart pixels even when the vertex count remains identical.
export async function validateRenderingReview(page,inspect,scenario){
 const save=()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.ExportDocument()));
 async function settled(before){
  await page.waitForFunction(previous=>{
   const s=JSON.parse(globalThis.chartBridge.Inspect());
   const rendered=s.backend.startsWith('OpenGL')?s.gpuSubmissions>previous.gpuSubmissions:s.overlayDraws>previous.overlayDraws;
   return rendered&&s.vertices>0&&!s.error;
  },before,{timeout:45000});
 }
 async function select(id){const previous=await inspect();await page.evaluate(id=>globalThis.chartBridge.Command('example',id),id);await settled(previous);}
 async function load(d){const previous=await inspect();await page.evaluate(json=>globalThis.chartBridge.ImportDocument(json),JSON.stringify(d));await settled(previous);}
 async function pixels(){
  await page.mouse.move(2,2);
  await page.evaluate(()=>new Promise(done=>requestAnimationFrame(()=>requestAnimationFrame(done))));
  const {bounds:b,plot:p}=await inspect();
  const clip={x:Math.ceil(b.x+p.x)+2,y:Math.ceil(b.y+p.y)+2,width:Math.floor(p.width)-4,height:Math.floor(p.height)-4};
  assert(clip.width>20&&clip.height>20);
  return PNG.sync.read(await page.screenshot({clip,scale:'css'}));
 }
 for(const id of ['surface','waterfall','bars3d','helix','mesh'])await scenario('review: '+id+' secondary axes preserve exact plot pixels',async()=>{
  await select(id);const d=await save(),before=await inspect(),reference=await pixels();
  const x={...d.xAxes[0],id:'review-x'},y={...d.yAxes[0],id:'review-y'};
  d.xAxes.push(x);d.yAxes.push(y);
  d.xAxes[0].minimum=1000;d.xAxes[0].maximum=2000;d.xAxes[0].scale='Linear';
  d.yAxes[0].minimum=1000;d.yAxes[0].maximum=2000;d.yAxes[0].scale='Linear';
  for(const s of d.series){s.xAxis=x.id;s.yAxis=y.id;}
  await load(d);const actual=await pixels();
  assert.equal((await inspect()).vertices,before.vertices);
  assert.equal(actual.width,reference.width);assert.equal(actual.height,reference.height);
  assert(actual.data.equals(reference.data),'A secondary axis must project identically to its equivalent primary axis');
  return {width:actual.width,height:actual.height,exactPixels:true};
 });
 await scenario('review: 3D grid toggle changes floor pixels and removes only grid vertices',async()=>{
  await select('surface');const before=await inspect(),visible=await pixels();
  await page.evaluate(()=>globalThis.chartBridge.Command('grid','false'));await settled(before);
  const after=await inspect(),hidden=await pixels();assert.equal(before.vertices-after.vertices,22*36);assert.equal(after.grid,false);
  assert(!visible.data.equals(hidden.data),'Grid toggle did not affect the displayed plot');
 });
 await scenario('review: waterfall width changes image and zero suppresses traces',async()=>{
  await select('waterfall');const d=await save(),before=await inspect(),normal=await pixels();
  d.series[0].strokeWidth=6;await load(d);const thick=await pixels();
  assert.equal((await inspect()).vertices,before.vertices);assert(!normal.data.equals(thick.data));
  d.series[0].strokeWidth=0;await load(d);assert((await inspect()).vertices<before.vertices);
 });
 await scenario('review: long missing intervals survive document rendering unchanged',async()=>{
  await select('signals');const d=await save();d.series=d.series.slice(0,1);d.showLegend=false;
  const y=Array(100000).fill(null);
  for(let i=0;i<5000;i++){y[i]=Math.sin(i*.01);y[95000+i]=Math.cos(i*.01);}
  d.series[0].data={kind:'uniform',start:0,step:1,y};d.xAxes[0].minimum=0;d.xAxes[0].maximum=99999;
  await load(d);const state=await inspect();assert.equal(state.sourcePoints,100000);assert(state.vertices<12000);
  assert.deepEqual((await save()).series[0].data.y,y,'Rendering must not rewrite missing samples');
  return {sourcePoints:state.sourcePoints,vertices:state.vertices};
 });
}
