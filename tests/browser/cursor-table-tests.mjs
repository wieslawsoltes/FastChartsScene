import assert from 'node:assert/strict';
import {resolve} from 'node:path';
import {readFile} from 'node:fs/promises';

export async function validateCursorTable(page,h){
 const {inspect,scenario,output,fixture,load,cursor,currentFrame,frames,stable}=h;
 const state=()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.InspectCursorTable()));
 async function action(name){
  if(name==='Cursor table')await page.evaluate(n=>globalThis.chartBridge.BringActionIntoView(n),name);
  const read=name==='Cursor table'?()=>page.evaluate(n=>JSON.parse(globalThis.chartBridge.ActionBounds(n)),name):()=>page.evaluate(n=>JSON.parse(globalThis.chartBridge.CursorTableBounds(n)),name);
  const b=await stable(read,name);await page.mouse.click(b.x+b.width/2,b.y+b.height/2);
 }
 async function sample(u=.4,v=.1,count=2){
  await currentFrame();const s=await inspect();await page.mouse.move(s.bounds.x+s.plot.x+u*s.plot.width,s.bounds.y+s.plot.y+v*s.plot.height);
  await page.waitForFunction(n=>{const s=JSON.parse(globalThis.chartBridge.InspectCursorTable());return s.active&&s.values.length===n;},count);
  await frames();return state();
 }
 async function setup(){
  const d=await fixture();d.series.push({kind:'line',title:'Average',visible:true,cursorTrackEnabled:true,color:[0,1,1,1],xAxis:'x',yAxis:'y',strokeWidth:2,interpolation:'Linear',marker:'None',markerSize:5,showLine:true,fillToBaseline:false,baseline:0,data:{kind:'xy',x:[4,8],y:[5,7]}});
  await load(d);if(!(await state()).enabled)await action('Cursor table');await currentFrame();return d;
 }
 await scenario('cursor table: native toggle displays all original financial and overlay values',async()=>{
  await setup();const s=await sample();assert.deepEqual(s.values.map(v=>v.title),['Original OHLC','Average']);
  assert.deepEqual(s.values.map(v=>v.data.y),[12,5]);assert.equal(s.values[0].candle.volume,12345);
  await page.waitForFunction(()=>{const s=JSON.parse(globalThis.chartBridge.InspectCursorTable());return s.paintedTexts.includes('Average')&&s.paintedTexts.includes('12')&&s.paintedTexts.includes('5');});
  await page.screenshot({path:resolve(output,'cursor-table.png'),scale:'css'});return {rows:s.values.length,originalValues:s.values.map(v=>v.data)};
 });
 await scenario('cursor table: vertical pointer movement retains row objects text and GPU uploads',async()=>{
  await setup();const before=await sample();const chart=await inspect(),uploads=await page.evaluate(()=>globalThis.FastChartsSceneWebGL.inspect().map(s=>s.uploads));
  for(let i=0;i<6;i++)await sample(.4,.12+i*.01);
  const after=await state();assert.equal(after.created,before.created);assert.equal(after.updated,before.updated);assert.equal(after.published,before.published);
  assert.equal((await inspect()).gpuSubmissions,chart.gpuSubmissions);assert.deepEqual(await page.evaluate(()=>globalThis.FastChartsSceneWebGL.inspect().map(s=>s.uploads)),uploads);
 });
 await scenario('cursor table: native Hold preserves copied values and CSV exports full source precision',async()=>{
  await setup();const before=await sample();await action('Hold cursor values');await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectCursorTable()).held);
  await sample(.8,.1);assert.deepEqual((await state()).values,before.values);
  const pending=page.waitForEvent('download');await action('Export cursor values');const file=await pending,path=resolve(output,'cursor-values.csv');await file.saveAs(path);
  const csv=(await readFile(path)).toString();assert(csv.includes('Original OHLC')&&csv.includes('Average')&&csv.includes('12345'));assert.equal(csv.split('\r\n').length,4);
  await action('Hold cursor values');await sample(.8,.1);assert.deepEqual((await state()).values.map(v=>v.data.x),[8,8]);
 });
 await scenario('cursor table: held snapshots reset on document replacement and tracking opt-out removes only its row',async()=>{
  const d=await setup();await sample();await action('Hold cursor values');await load(d);assert(!(await state()).held);
  d.series[1].cursorTrackEnabled=false;await load(d);const s=await sample(.4,.1,1);assert.equal(s.values[0].title,'Original OHLC');
  await action('Cursor table');assert(!(await state()).enabled);assert.equal((await state()).values.length,0);
 });
 await scenario('cursor table: large native lists virtualize rows and real scrolling reaches the last series',async()=>{
  const d=await setup(),line=structuredClone(d.series[1]);d.series=Array.from({length:128},(_,i)=>({...structuredClone(line),title:'Channel '+i}));await load(d);const s=await sample(.4,.1,128);
  assert(s.realized>0&&s.realized<40,'List must virtualize its native containers');
  const b=await stable(()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.CursorTableBounds('Same-pane cursor results'))),'results list');
  await page.mouse.move(b.x+b.width/2,b.y+b.height/2);
  for(let i=0;i<4;i++){await page.mouse.wheel(0,1300);await frames();}
  const last=await stable(()=>page.evaluate(()=>{const b=globalThis.chartBridge.CursorTableRowBounds(127);return b?JSON.parse(b):null;}),'last result');
  await page.mouse.click(last.x+100,last.y+last.height/2);await page.waitForFunction(()=>globalThis.chartBridge.CursorTableDetails().includes('Channel 127'));
  assert((await state()).realized<40);return {rows:128,realized:(await state()).realized};
 });
 await scenario('cursor table: compact viewport retains usable chart and horizontally scrollable native results',async()=>{
  await setup();await page.setViewportSize({width:480,height:800});await sample();
  const s=await inspect(),b=await page.evaluate(()=>JSON.parse(globalThis.chartBridge.CursorTableBounds('$table')));
  assert(s.bounds.height>250&&b.width<=480&&b.y+b.height<=800);assert.equal((await state()).values.length,2);
  await page.screenshot({path:resolve(output,'cursor-table-mobile.png'),scale:'css'});
  await page.setViewportSize({width:1440,height:960});await currentFrame();await action('Cursor table');
 });
 await scenario('cursor table: row reuse and reentrant financial callbacks never publish obsolete values',async()=>{
  assert.equal(await page.evaluate(()=>globalThis.chartBridge.CheckCursorTableReuse()),'passed');
  await setup();await page.evaluate(()=>globalThis.chartBridge.ArmCursorTableReview());
  // The financial hit event disables the overlay line before the all-series batch.
  try {
   await sample(.4,.4,1);
   assert.equal(await page.evaluate(()=>globalThis.chartBridge.FinishCursorTableReview()),'passed');
  } finally { await page.evaluate(()=>globalThis.chartBridge.CancelCursorTableReview()); }
  assert.equal((await state()).values[0].title,'Original OHLC');await action('Cursor table');
 });
}
