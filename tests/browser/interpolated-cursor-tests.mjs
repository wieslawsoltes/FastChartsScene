import assert from 'node:assert/strict';
import {resolve} from 'node:path';
import {readFile} from 'node:fs/promises';

// Native input drives every mode/hold/export transition. Probes only read bounds,
// values and real native text, except for explicit isolated contract/reentry fixtures.
export async function validateInterpolatedCursors(page,h){
 const {inspect,scenario,output,setup,sample,action,state,currentFrame,stable,load}=h;
 const mode=()=>page.evaluate(()=>globalThis.chartBridge.CursorTrackingMode());
 const saved=()=>page.evaluate(()=>globalThis.chartBridge.ExportDocument());
 const near=(actual,expected)=>assert(Math.abs(actual-expected)<1e-9,`${actual} != ${expected}`);
 async function choose(value,keyboard=false){
  if(await mode()===`${value}/${value}`)return;
  await action('Cursor tracking mode');
  if(keyboard){await page.keyboard.press(value==='NearestX'?'Home':'End');await page.keyboard.press('Enter');}
  else{
   const index=value==='NearestX'?0:1;
   const item=await stable(()=>page.evaluate(i=>{const b=globalThis.chartBridge.CursorTrackingOptionBounds(i);return b?JSON.parse(b):null;},index),'cursor mode option');
   await page.mouse.click(item.x+item.width/2,item.y+item.height/2);
  }
  await page.waitForFunction(m=>globalThis.chartBridge.CursorTrackingMode()===`${m}/${m}`,value);
 }
 function csvRows(text){
  const rows=[];let row=[],cell='',quoted=false;
  for(let i=0;i<text.length;i++){
   const c=text[i];
   if(quoted){if(c==='"'&&text[i+1]==='"'){cell+='"';i++;}else if(c==='"')quoted=false;else cell+=c;}
   else if(c==='"')quoted=true;
   else if(c===','){row.push(cell);cell='';}
   else if(c==='\n'){row.push(cell.endsWith('\r')?cell.slice(0,-1):cell);rows.push(row);row=[];cell='';}
   else cell+=c;
  }
  assert(!quoted&&row.length===0&&cell==='','CSV must end in a complete record');
  return rows.slice(1).map(row=>{assert.equal(row.length,rows[0].length);return Object.fromEntries(rows[0].map((key,i)=>[key,row[i]]));});
 }
 await scenario('interpolated cursor: native selector paints calculated values and preserves original endpoints',async()=>{
  await setup();assert.equal(await mode(),'NearestX/NearestX');await sample(.62,.1);const document=await saved();
  await choose('Interpolated');const s=await sample(.6,.1),value=s.values[1];
  assert(value.isInterpolated);near(value.resultData.x,6);near(value.resultData.y,6);
  // PointD's diagnostic JSON includes its public computed IsFinite property.
  // Retain that contract rather than changing production serialization for the test.
  assert.deepEqual(value.data,{x:4,y:5,isFinite:true});assert.deepEqual(value.interpolation.endData,{x:8,y:7,isFinite:true});assert.equal(value.index,0);assert.equal(value.interpolation.endIndex,1);
  assert.equal(value.resultData.isFinite,true);assert.deepEqual(value.interpolation.result,value.resultData);
  assert(!s.values[0].isInterpolated);assert.equal(await saved(),document);
  await page.waitForFunction(()=>{const s=JSON.parse(globalThis.chartBridge.InspectCursorTable());return s.paintedTexts.includes('Calc Y')&&s.paintedTexts.includes('0–1')&&s.paintedTexts.includes('Result value')&&s.paintedTexts.includes('6');});
  await page.screenshot({path:resolve(output,'cursor-table-interpolated.png'),scale:'css'});
  return {source:value.data,result:value.resultData,end:value.interpolation.endData};
 });
 await scenario('interpolated cursor: native Hold and CSV retain both source endpoints and separate results',async()=>{
  await setup();await choose('Interpolated');const before=await sample(.62,.1);await action('Hold cursor values');
  await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectCursorTable()).held);await sample(.75,.1);
  assert.deepEqual((await state()).values,before.values);
  const pending=page.waitForEvent('download');await action('Export cursor values');const file=await pending,path=resolve(output,'interpolated-cursor-values.csv');await file.saveAs(path);
  const rows=csvRows((await readFile(path)).toString()),line=rows.find(r=>r.SeriesTitle==='Average'),financial=rows.find(r=>r.SeriesTitle==='Original OHLC');
  assert.equal(line.ResultKind,'Interpolated');assert.equal(line.Index,'0');assert.equal(line.X,'4');assert.equal(line.Value,'5');assert.equal(line.EndIndex,'1');assert.equal(line.EndX,'8');assert.equal(line.EndValue,'7');assert.equal(line.Interpolation,'Linear');
  assert.equal(Number(line.ResultX),before.values[1].resultData.x);assert.equal(Number(line.ResultValue),before.values[1].resultData.y);
  assert.equal(financial.ResultKind,'Original');assert.equal(financial.Volume,'12345');assert.equal(financial.EndIndex,'');
  await action('Hold cursor values');const live=await sample(.75,.1);near(live.values[1].resultData.x,7.5);near(live.values[1].resultData.y,6.75);
 });
 await scenario('interpolated cursor: vertical motion retains rows formatted values and geometry uploads',async()=>{
  await setup();await choose('Interpolated');const before=await sample(.62,.1);const chart=await inspect(),uploads=await page.evaluate(()=>globalThis.FastChartsSceneWebGL.inspect().map(s=>s.uploads));
  for(let i=0;i<6;i++)await sample(.62,.11+i*.01);
  const after=await state();assert.equal(after.created,before.created);assert.equal(after.updated,before.updated);assert.equal(after.published,before.published);assert.deepEqual(after.values,before.values);
  assert.equal((await inspect()).gpuSubmissions,chart.gpuSubmissions);assert.deepEqual(await page.evaluate(()=>globalThis.FastChartsSceneWebGL.inspect().map(s=>s.uploads)),uploads);
  assert(await page.evaluate(()=>globalThis.chartBridge.CursorTrackingTests())<=8);
 });
 await scenario('interpolated cursor: native results follow step methods and logarithmic inverted axes',async()=>{
  const d=await setup();await choose('Interpolated');
  for(const [method,value] of [['StepBefore',7],['StepAfter',5]]){
   d.series[1].interpolation=method;await load(d);const s=await sample(.6,.1);assert(s.values[1].isInterpolated);assert.equal(s.values[1].resultData.y,value);
  }
  d.series[1].interpolation='Linear';d.series[1].data={kind:'xy',x:[1,100],y:[2,20]};
  d.xAxes[0].minimum=1;d.xAxes[0].maximum=100;d.xAxes[0].scale='Logarithmic';d.xAxes[0].inverted=true;d.yAxes[0].inverted=true;
  await load(d);const s=await sample(.5,.9);near(s.values[1].resultData.x,10);near(s.values[1].resultData.y,11);assert.deepEqual(s.values[1].data,{x:1,y:2,isFinite:true});
 });
 await scenario('interpolated cursor: compact native keyboard selector clears Hold without changing the document',async()=>{
  await setup();await choose('Interpolated');await page.setViewportSize({width:480,height:800});await sample(.62,.1);
  const document=await saved(),b=await stable(()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.CursorTableBounds('Cursor tracking mode'))),'compact mode selector');
  assert(b.x>=0&&b.x+b.width<=480&&b.height>=20);
  await action('Hold cursor values');await choose('NearestX',true);assert(!(await state()).held);
  const original=await sample(.62,.1);assert(original.values.every(v=>!v.isInterpolated));assert.equal(await saved(),document);
  await choose('Interpolated');await sample(.62,.1);await page.screenshot({path:resolve(output,'cursor-table-interpolated-mobile.png'),scale:'css'});
  await choose('NearestX',true);await page.setViewportSize({width:1440,height:960});await currentFrame();
 });
 await scenario('interpolated cursor: native contracts and reentrant same-owner mode changes retain the newer batch',async()=>{
  assert.equal(await page.evaluate(()=>globalThis.chartBridge.CheckInterpolatedCursorContracts()),'passed');
  await setup();await choose('NearestX');await page.evaluate(()=>globalThis.chartBridge.ArmCursorModeReentry());await currentFrame();
  try{
   const result=await sample(.62,.1);assert(result.values.every(v=>!v.isInterpolated));
   assert.equal(await page.evaluate(()=>globalThis.chartBridge.FinishCursorModeReentry()),'passed');assert.equal(await mode(),'NearestX/NearestX');
  }finally{await page.evaluate(()=>globalThis.chartBridge.CancelCursorModeReentry());}
  await action('Cursor table');
 });
}
