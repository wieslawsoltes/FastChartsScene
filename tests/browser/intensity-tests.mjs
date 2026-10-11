import assert from 'node:assert/strict';
import {PNG} from 'pngjs';
import {resolve} from 'node:path';
import {intensityInput} from './intensity-input.mjs';

export async function validateIntensity(page,{inspect,select,scenario,output}) {
 const state=()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.InspectIntensity()));
 const save=()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.ExportDocument()));
 const painted=s=>s.backend.startsWith('OpenGL')?s.gpuSubmissions:s.overlayDraws;
 async function settle(before){
  await page.waitForFunction(previous=>{
   const s=JSON.parse(globalThis.chartBridge.Inspect());
   const rendered=s.backend.startsWith('OpenGL')?s.gpuSubmissions>previous.gpuSubmissions:s.overlayDraws>previous.overlayDraws;
   return s.vertices>0&&rendered&&!s.error;
  },before,{timeout:45000});
 }
 async function importDocument(document){
  const before=await inspect();await page.evaluate(json=>globalThis.chartBridge.ImportDocument(json),JSON.stringify(document));await settle(before);
 }
 async function fixture(values=[1,2,3,4,5,6,7,8,9]){
  await select('heatmap');const d=await save();d.title='Calibrated scalar cells';d.showGrid=false;d.showLegend=false;
  d.xAxes[0].minimum=0;d.xAxes[0].maximum=4;d.yAxes[0].minimum=0;d.yAxes[0].maximum=4;
  const heat=d.series[0];heat.grid={columns:3,rows:3,xMinimum:0,xMaximum:4,yMinimum:0,yMaximum:4,values};
  heat.colorBar={visible:true,title:'Power',units:'mW'};
  heat.colorScale={mode:'Linear',reversed:false,outOfRange:'Clamp',missingColor:[0,0,0,0],range:[0,10],palette:{name:'Grayscale',interpolation:'Gradient',stops:[{offset:0,color:[0,0,0,1]},{offset:1,color:[1,1,1,1]}]}};
  await importDocument(d);return d;
 }
 async function pointAt(x,y){
  const s=await inspect(),d=await save(),a=d.xAxes[0],b=d.yAxes[0];
  function unit(v,axis){let u=axis.scale==='Logarithmic'?Math.log(v/axis.minimum)/Math.log(axis.maximum/axis.minimum):(v-axis.minimum)/(axis.maximum-axis.minimum);return axis.inverted?1-u:u;}
  return{x:s.bounds.x+s.plot.x+unit(x,a)*s.plot.width,y:s.bounds.y+s.plot.y+(1-unit(y,b))*s.plot.height};
 }
 async function hover(x,y){const p=await pointAt(x,y);await page.mouse.move(p.x,p.y);await page.waitForTimeout(40);return state();}
 async function pixelAt(x,y,name){
  await page.mouse.move(2,2);await page.evaluate(()=>new Promise(done=>requestAnimationFrame(()=>requestAnimationFrame(done))));
  const point=await pointAt(x,y);const png=PNG.sync.read(await page.screenshot({path:resolve(output,name+'.png'),scale:'css'}));
  const i=(Math.floor(point.y)*png.width+Math.floor(point.x))*4;return [...png.data.subarray(i,i+4)];
 }
 function color(actual,expected){for(let i=0;i<3;i++)assert(Math.abs(actual[i]-expected[i])<=2,`Expected ${expected}; got ${actual}`);}
 const {click,selectOption,input}=intensityInput(page,state);
 await scenario('intensity colorbar lives outside data plot and exposes range',async()=>{
  await select('heatmap');const s=await inspect(),i=await state(),bar=i.series[0].colorBar;
  assert(bar&&bar.x>s.plot.x+s.plot.width&&bar.x+bar.width<s.bounds.width);assert(i.series[0].hasValues);
  await page.screenshot({path:resolve(output,'intensity-colorbar.png'),scale:'css'});
 });
 await scenario('intensity cursor returns exact sample coordinates and visible cell',async()=>{
  await fixture();const i=await hover(2.2,1.6);assert.equal(i.cursor.value,5);assert.equal(i.cursor.column,1);assert.equal(i.cursor.row,1);assert.deepEqual(i.cursor.data,{x:2,y:2,isFinite:true});assert.equal(i.cursor.color,'#808080FF');
  await page.screenshot({path:resolve(output,'intensity-cursor.png'),scale:'css'});
 });
 await scenario('intensity configured palette produces measured pixel colors',async()=>{
  await fixture();color(await pixelAt(2.2,1.6,'intensity-gray'),[128,128,128]);
  const before=await inspect();await click('Reverse palette');await settle(before);assert((await state()).series[0].reversed);
  color(await pixelAt(.2,.2,'intensity-reversed'),[230,230,230]);
 });
 await scenario('intensity preset palette editor changes model and pixels',async()=>{
  await fixture();const before=await inspect();await selectOption('Palette',3);await settle(before);assert.equal((await state()).series[0].palette,'Thermal');
  const rgb=await pixelAt(2.2,1.6,'intensity-thermal');assert(Math.max(...rgb.slice(0,3))-Math.min(...rgb.slice(0,3))>60);
 });
 await scenario('intensity custom stepped palette uses exact thresholds',async()=>{
  await fixture();const before=await inspect();await selectOption('Palette interpolation',1);await settle(before);
  await input('Custom stops: offset, color','0, #FF0000\n0.5, #00FF00\n1, #0000FF');const old=await inspect();await click('Apply custom palette');await settle(old);
  const i=await state();assert.equal(i.series[0].palette,'Custom');assert.equal(i.series[0].interpolation,'Stepped');assert.equal(i.editorError,null);
  color(await pixelAt(.2,.2,'intensity-custom-low'),[255,0,0]);color(await pixelAt(2.2,1.6,'intensity-custom-threshold'),[0,255,0]);
 });
 await scenario('intensity invalid custom palette does not alter saved chart',async()=>{
  await fixture();const before=await save();await input('Custom stops: offset, color','0, #FF0000\n0, #00FF00\n1, #0000FF');await click('Apply custom palette');
  assert((await state()).editorError);assert.deepEqual(await save(),before);assert.equal((await inspect()).error,null);
 });
 await scenario('intensity manual range editor validates and commits both bounds',async()=>{
  await fixture();await input('Color minimum','4');await input('Color maximum','6');const before=await inspect();await click('Apply color range');await settle(before);
  let i=await state();assert.equal(i.series[0].minimum,4);assert.equal(i.series[0].maximum,6);assert(!i.series[0].autoRange);
  await input('Color minimum','9');await input('Color maximum','2');await click('Apply color range');i=await state();assert(i.editorError);assert.equal(i.series[0].minimum,4);assert.equal(i.series[0].maximum,6);
 });
 await scenario('intensity auto-range and colorbar visibility controls reflow chart',async()=>{
  await fixture();let before=await inspect();await click('Automatic color range');await settle(before);
  const i=await state();assert(i.series[0].autoRange);assert.equal(i.series[0].minimum,1);assert.equal(i.series[0].maximum,9);
  before=await inspect();await click('Show colorbar');await settle(before);assert(!(await state()).series[0].colorBar);assert((await inspect()).plot.width>before.plot.width);
 });
 await scenario('intensity logarithmic mapping rejects invalid manual domain',async()=>{
  await fixture();const saved=await save();await selectOption('Color scale',1);let i=await state();assert(i.editorError);assert.equal(i.series[0].mode,'Linear');assert.deepEqual(await save(),saved);
  let before=await inspect();await click('Automatic color range');await settle(before);before=await inspect();await selectOption('Color scale',1);await settle(before);
  i=await state();assert.equal(i.series[0].mode,'Logarithmic');assert.equal(i.editorError,null);assert.equal(i.series[0].minimum,1);
 });
 await scenario('intensity missing cells stay empty or report painted missing values',async()=>{
  const d=await fixture([1,2,3,4,null,6,7,8,9]);let i=await hover(2,2);assert.equal(i.cursor,null);
  d.series[0].colorScale.missingColor=[1,0,1,1];await importDocument(d);i=await hover(2,2);assert.equal(i.cursor.value,null);assert.equal(i.cursor.color,'#FF00FFFF');
  color(await pixelAt(2.2,1.6,'intensity-missing-opaque'),[255,0,255]);
 });
 await scenario('intensity inverse and logarithmic axes pick the correct original cell',async()=>{
  const d=await fixture();d.xAxes[0].inverted=true;d.yAxes[0].inverted=true;await importDocument(d);let i=await hover(3.8,.2);assert.equal(i.cursor.value,3);assert.equal(i.cursor.column,2);
  d.xAxes[0].inverted=false;d.xAxes[0].scale='Logarithmic';d.xAxes[0].minimum=.2;d.xAxes[0].maximum=4;await importDocument(d);i=await hover(.5,.2);assert.equal(i.cursor.value,1);assert.equal(i.cursor.column,0);
 });
 await scenario('intensity empty and constant auto-ranges remain truthful',async()=>{
  const d=await fixture(Array(9).fill(null));d.series[0].colorScale.range=null;await importDocument(d);let i=await state();assert(!i.series[0].hasValues);assert.equal(i.series[0].minimum,null);assert.equal(i.series[0].maximum,null);
  d.series[0].grid.values=Array(9).fill(17);await importDocument(d);i=await state();assert.equal(i.series[0].minimum,17);assert.equal(i.series[0].maximum,17);assert.equal((await hover(2,2)).cursor.value,17);
  await page.screenshot({path:resolve(output,'intensity-constant.png'),scale:'css'});
 });
 await scenario('intensity shared scales keep separate pane colorbars',async()=>{
  await select('intensity-panes');const i=await state();assert.equal(i.series.length,2);assert.equal(i.series[0].minimum,i.series[1].minimum);assert.equal(i.series[0].maximum,i.series[1].maximum);
  const a=i.series[0].colorBar,b=i.series[1].colorBar;assert(a&&b&&a.y+a.height<b.y);
  await page.screenshot({path:resolve(output,'intensity-panes.png'),scale:'css'});
 });
 await scenario('intensity settings and colorbars survive document roundtrip',async()=>{
  await select('color-scales');const saved=await save(),expected=await state();await select('signals');await importDocument(saved);
  assert.deepEqual(await save(),saved);const actual=await state();assert.equal(actual.series[0].mode,expected.series[0].mode);assert.deepEqual(actual.series[0].colorBar,expected.series[0].colorBar);
 });
}
