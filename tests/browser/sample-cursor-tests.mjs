import assert from 'node:assert/strict';
import {resolve} from 'node:path';
import {mkdir} from 'node:fs/promises';

// Read-only diagnostics inspect the public cursor state. All cursor delivery uses real mouse
// events; document setup uses the same bounded JSON import API as the explorer's Open command.
export async function validateSampleCursors(page,inspect,scenario){
 const save=()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.ExportDocument()));
 const cursor=()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.InspectSampleCursor()));
 const output=resolve(process.env.TEST_OUTPUT??(process.env.RENDER_MODE==='Skia'?'../../artifacts/fallback-results':'../../artifacts/browser-results'));
 await mkdir(output,{recursive:true});
 async function settled(before){
  await page.waitForFunction(previous=>{
   const s=JSON.parse(globalThis.chartBridge.Inspect());
   const rendered=s.backend.startsWith('OpenGL')?s.gpuSubmissions>previous.gpuSubmissions:s.overlayDraws>previous.overlayDraws;
   return rendered&&s.vertices>0&&!s.error;
  },before,{timeout:45000});
 }
 async function load(d){const previous=await inspect();await page.evaluate(json=>globalThis.chartBridge.ImportDocument(json),JSON.stringify(d));await settled(previous);}
 async function select(id){const before=await inspect();await page.evaluate(id=>globalThis.chartBridge.Command('example',id),id);await settled(before);}
 async function fixture(){
  await select('signals');const d=await save();d.title='Indexed original-sample cursor';d.showLegend=false;d.showGrid=true;
  d.xAxes[0].minimum=0;d.xAxes[0].maximum=10;d.yAxes[0].minimum=0;d.yAxes[0].maximum=10;
  const s=d.series[0];d.series=[s];s.title='Dense cloud';s.showLine=false;s.fillToBaseline=false;s.marker='Circle';s.markerSize=3;
  const x=[4],y=[6];for(let i=1;i<12000;i++){x.push(8+((i*7919)%11999)/12000);y.push(.2+((i*379)%11999)/6000);}
  s.data={kind:'xy',x,y};await load(d);return d;
 }
 async function pointAt(x,y){
  const s=await inspect(),d=await save(),a=d.xAxes.find(a=>a.id===d.series[0].xAxis),b=d.yAxes.find(a=>a.id===d.series[0].yAxis);
  const unit=(v,axis)=>{const u=axis.scale==='Logarithmic'?Math.log(v/axis.minimum)/Math.log(axis.maximum/axis.minimum):(v-axis.minimum)/(axis.maximum-axis.minimum);return axis.inverted?1-u:u;};
  return{x:s.bounds.x+s.plot.x+unit(x,a)*s.plot.width,y:s.bounds.y+s.plot.y+(1-unit(y,b))*s.plot.height};
 }
 async function hover(x,y,expectedIndex=0){
  const p=await pointAt(x,y);await page.mouse.move(p.x,p.y);
  await page.waitForFunction(index=>JSON.parse(globalThis.chartBridge.InspectSampleCursor()).hit?.index===index,expectedIndex,{timeout:10000});
  return cursor();
 }
 await scenario('cursor: dense cloud returns original index with bounded comparisons',async()=>{
  await fixture();const result=await hover(4,6);assert.equal(result.hit.data.x,4);assert.equal(result.hit.data.y,6);assert.equal(result.indexBuilds,1);
  assert(result.testedSamples<512,`Tested ${result.testedSamples} samples`);assert(result.hit.distance<2);
  await page.screenshot({path:resolve(output,'sample-indexed-cursor.png'),scale:'css'});return result;
 });
 await scenario('cursor: retained index survives theme zoom and original-source export',async()=>{
  const d=await fixture();await hover(4,6);const before=await inspect();const builds=(await cursor()).indexBuilds;
  await page.evaluate(()=>globalThis.chartBridge.Command('theme','light'));await settled(before);await hover(4,6);
  const p=await pointAt(4,6);await page.mouse.move(p.x,p.y);await page.mouse.wheel(0,-120);
  await page.waitForFunction(()=>{const s=JSON.parse(globalThis.chartBridge.Inspect());return s.axes.find(a=>a.id==='x').maximum-s.axes.find(a=>a.id==='x').minimum<10;});
  await hover(4,6);assert.equal((await cursor()).indexBuilds,builds);assert.deepEqual((await save()).series[0].data,d.series[0].data);
 });
 await scenario('cursor: cursor-only movement never reuploads geometry',async()=>{
  await fixture();await hover(4,6);const before=await inspect();
  const stats=await page.evaluate(()=>globalThis.FastChartsSceneWebGL.inspect());
  const p=await pointAt(4,6);for(let i=1;i<=6;i++)await page.mouse.move(p.x+i*.6,p.y);
  await page.evaluate(()=>new Promise(done=>requestAnimationFrame(()=>requestAnimationFrame(done))));
  const after=await inspect(),next=await page.evaluate(()=>globalThis.FastChartsSceneWebGL.inspect());
  assert.equal(after.gpuSubmissions,before.gpuSubmissions);assert.deepEqual(next.map(s=>s.uploads),stats.map(s=>s.uploads));assert((await cursor()).hit);
 });
 await scenario('cursor: duplicate centers prefer topmost series then first source index',async()=>{
  const d=await fixture();d.series[0].data={kind:'xy',x:Array(5000).fill(4),y:Array(5000).fill(6)};
  d.series.push(structuredClone(d.series[0]));d.series[1].title='Top duplicate';await load(d);
  let hit=(await hover(4,6)).hit;assert.equal(hit.title,'Top duplicate');assert.equal(hit.index,0);
  d.series[1].color[3]=0;await load(d);hit=(await hover(4,6)).hit;assert.equal(hit.title,'Dense cloud');
  d.series[0].visible=false;await load(d);const p=await pointAt(4,6);await page.mouse.move(p.x,p.y);await page.evaluate(()=>new Promise(requestAnimationFrame));assert.equal((await cursor()).hit,null);
 });
 await scenario('cursor: legend overlay excludes an otherwise pickable sample',async()=>{
  const d=await fixture();d.series[0].data.x[0]=9;d.series[0].data.y[0]=9.7;d.showLegend=true;await load(d);
  const p=await pointAt(9,9.7);await page.mouse.move(p.x,p.y);await page.evaluate(()=>new Promise(requestAnimationFrame));assert.equal((await cursor()).hit,null);
  d.showLegend=false;await load(d);assert.equal((await hover(9,9.7)).hit.data.y,9.7);
 });
 await scenario('cursor: logarithmic inverted secondary bindings keep exact original values',async()=>{
  const d=await fixture();d.series[0].data.x[0]=1.2;d.series[0].data.y[0]=.8;
  const x={...d.xAxes[0],id:'cursor-x',minimum:.1,maximum:10,scale:'Logarithmic',inverted:true};
  const y={...d.yAxes[0],id:'cursor-y',minimum:.1,maximum:10,scale:'Logarithmic',inverted:true};
  d.xAxes.push(x);d.yAxes.push(y);d.xAxes[0].minimum=100;d.xAxes[0].maximum=200;d.yAxes[0].minimum=100;d.yAxes[0].maximum=200;
  d.series[0].xAxis=x.id;d.series[0].yAxis=y.id;await load(d);const hit=(await hover(1.2,.8)).hit;
  assert.equal(hit.data.x,1.2);assert.equal(hit.data.y,.8);assert.equal(hit.index,0);
 });
 await scenario('cursor: replacement and leaving the chart clear public cursor state',async()=>{
  const d=await fixture();await hover(4,6);d.title='Replaced';await load(d);assert.equal((await cursor()).hit,null);
  await hover(4,6);await page.mouse.move(2,2);await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectSampleCursor()).hit===null);
  await select('surface');assert.equal((await cursor()).hit,null);
 });
 await scenario('cursor: bounded streaming uses candidate searches rather than rebuilding a full index',async()=>{
  await select('signals');
  try{
   await page.evaluate(()=>globalThis.chartBridge.Command('stream','true'));
   await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.Inspect()).sourcePoints===60000);
   const t=3,amplitude=Math.sin(t*7)*(.7+.22*Math.sin(t*.8));const p=await pointAt(t,amplitude);await page.mouse.move(p.x,p.y);
   await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectSampleCursor()).hit!==null);
   const state=await cursor();assert.equal(state.indexBuilds,0);assert(state.testedSamples<4000);return state;
  }finally{await page.evaluate(()=>globalThis.chartBridge.Command('stream','false'));}
 });
}
