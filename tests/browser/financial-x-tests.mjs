import assert from 'node:assert/strict';
import {resolve} from 'node:path';
import {execFileSync} from 'node:child_process';

// Run in the existing exact-build suite, under both renderers and device scales.
// The snap selector uses native pointer input. Imported charts are explicit fixtures;
// diagnostics only read frame geometry and the real public cursor result.
export async function validateFinancialX(page,h){
 const {inspect,scenario,output,fixture,load,hover,cursor,saved,currentFrame,frames,property,stable,sourcePoint,paintedReadout,chooseField,download}=h;
 async function choose(mode){
  const b=await property('CursorSnapMode');await page.mouse.click(b.x+b.width/2,b.y+b.height/2);
  const index=mode==='NearestX'?1:0;
  const item=await stable(()=>page.evaluate(i=>{const s=globalThis.chartBridge.PropertyOptionBounds('Series','CursorSnapMode',i);return s?JSON.parse(s):null;},index),'Financial snap option');
  await page.mouse.click(item.x+item.width/2,item.y+item.height/2);
  await page.waitForFunction(mode=>JSON.parse(globalThis.chartBridge.ExportDocument()).series[0].cursorSnapMode===mode,mode);
  await currentFrame();
 }
 async function move(u,v,index=null){
  await currentFrame();const s=await inspect();await page.mouse.move(s.bounds.x+s.plot.x+u*s.plot.width,s.bounds.y+s.plot.y+v*s.plot.height);
  if(index!==null){
   await page.waitForFunction(index=>{const s=JSON.parse(globalThis.chartBridge.InspectFinancialCursor());return s.hit?.index===index&&s.hit.snapMode==='NearestX'&&s.readoutBounds?.width>0;},index);
  }
  await frames();return cursor();
 }
 await scenario('financial X: native mode selector follows original prices away from candle geometry',async()=>{
  const original=await fixture();assert.equal((await move(.41,.07)).hit,null);
  await choose('NearestX');const result=await move(.41,.07,0);
  assert(result.hit.distance>100);assert.equal(result.hit.data.x,4);assert.equal(result.hit.data.y,12);
  assert(result.readout.includes('Nearest X  4.00'));assert.deepEqual(result.hit.sample,{x:4,open:10,high:16,low:8,close:12,volume:12345});
  assert.deepEqual((await saved()).series[0].candles,original.series[0].candles);
  await page.screenshot({path:resolve(output,'financial-x-readout.png'),scale:'css'});
  await chooseField(1,'High');assert.equal((await move(.41,.07,0)).hit.data.y,16);
  await choose('NearestPoint');assert.equal((await move(.41,.07)).hit,null);
  return {sourceIndex:result.hit.index,source:result.hit.data,distance:result.hit.distance};
 });
 await scenario('financial X: duplicate coordinates prefer closest visible price then original index',async()=>{
  const d=await fixture();d.series[0].cursorSnapMode='NearestX';
  d.series[0].candles=[[4,10,20,0,6,11],[4,10,20,0,15,12],[4,10,20,0,15,13],[4.1,25,30,24,26,14]];await load(d);
  const upper=await move(.41,.2,1);assert.equal(upper.hit.sample.volume,12);
  const lower=await move(.41,.71,0);assert.equal(lower.hit.sample.volume,11);
  assert.deepEqual((await saved()).series[0].candles,d.series[0].candles);
 });
 await scenario('financial X: mode changes reuse the source index and pointer-only movement retains uploads and text',async()=>{
  await fixture(true);const nearest=await hover();assert.equal(nearest.indexBuilds,1);
  await choose('NearestX');const initial=await move(.41,.08,0);assert.equal(initial.indexBuilds,nearest.indexBuilds);
  const before=await inspect(),uploads=await page.evaluate(()=>globalThis.FastChartsSceneWebGL.inspect().map(s=>s.uploads));
  for(let i=0;i<6;i++)await move(.410+i*.001,.10+i*.01,0);
  const result=await cursor(),after=await inspect();assert.equal(result.indexBuilds,1);assert.equal(result.textFormats,initial.textFormats);
  assert.equal(after.gpuSubmissions,before.gpuSubmissions);
  assert.deepEqual(await page.evaluate(()=>globalThis.FastChartsSceneWebGL.inspect().map(s=>s.uploads)),uploads);
  assert(result.testedSamples<512);return {indexBuilds:result.indexBuilds,testedSamples:result.testedSamples,textFormats:result.textFormats};
 });
 await scenario('financial X: logarithmic inverted secondary panes keep price tracking and readout bounded',async()=>{
  const d=await fixture();d.layout='Stacked';d.series[0].cursorSnapMode='NearestX';
  d.xAxes.push({...d.xAxes[0],id:'time-x',minimum:1,maximum:100,scale:'Logarithmic',inverted:true});
  d.yAxes.push({...d.yAxes[0],id:'price-y',minimum:1,maximum:20,scale:'Logarithmic',inverted:true});
  d.series[0].xAxis='time-x';d.series[0].yAxis='price-y';await load(d);
  const source=await sourcePoint();await page.mouse.move(source.x,source.y);await paintedReadout();
  let result=await cursor();const pane=result.hit.paneBounds;const chart=await inspect();
  await page.mouse.move(source.x,chart.bounds.y+pane.y+6);await paintedReadout();await frames();result=await cursor();
  assert.equal(result.hit.index,0);assert.equal(result.hit.snapMode,'NearestX');assert.equal(result.hit.data.y,12);
  const b=result.readoutBounds;assert(b.x>=pane.x&&b.y>=pane.y&&b.x+b.width<=pane.x+pane.width&&b.y+b.height<=pane.y+pane.height);
  await page.mouse.move(source.x,chart.bounds.y+chart.plot.y+5);await frames();assert.equal((await cursor()).hit,null);
 });
 await scenario('financial X: chart and extracted-project downloads preserve the native mode and reject invalid imports',async()=>{
  await fixture();await choose('NearestX');await move(.41,.07,0);const document=await saved();
  const json=JSON.parse((await download('Save chart','financial-x.fcscene.json')).toString());assert.deepEqual(json,document);
  await download('Extract project','financial-x-project.zip');
  const embedded=execFileSync('unzip',['-p',resolve(output,'financial-x-project.zip'),'chart.json'],{encoding:'utf8'});assert.deepEqual(JSON.parse(embedded),document);
  const bad=structuredClone(document);bad.series[0].cursorSnapMode='Unknown';
  await assert.rejects(()=>page.evaluate(json=>globalThis.chartBridge.ImportDocument(JSON.stringify(json)),bad));assert.deepEqual(await saved(),document);
  await load(document);assert.equal((await move(.41,.07,0)).hit.snapMode,'NearestX');
 });
 await scenario('financial X: compact readout remains inside the chart and mode survives viewport changes',async()=>{
  await fixture();await choose('NearestX');const document=await saved();
  await page.setViewportSize({width:480,height:800});await currentFrame();
  const result=await move(.43,.06,0),b=result.readoutBounds,pane=result.hit.paneBounds;
  assert(b.x>=pane.x&&b.y>=pane.y&&b.x+b.width<=pane.x+pane.width&&b.y+b.height<=pane.y+pane.height);
  assert.deepEqual(await saved(),document);await page.screenshot({path:resolve(output,'financial-x-mobile.png'),scale:'css'});
  await page.mouse.move(2,2);await page.waitForFunction(()=>{const s=JSON.parse(globalThis.chartBridge.InspectFinancialCursor());return !s.hit&&!s.readout&&!s.readoutBounds;});
  await page.setViewportSize({width:1440,height:960});await currentFrame();await move(.43,.06,0);assert.deepEqual(await saved(),document);
 });
}
