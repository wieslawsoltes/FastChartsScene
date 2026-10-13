import assert from 'node:assert/strict';
import {PNG} from 'pngjs';
import {validateCursorTable} from './cursor-table-tests.mjs';
import {validateFinancialX} from './financial-x-tests.mjs';
import {resolve} from 'node:path';
import {mkdir,readFile} from 'node:fs/promises';

// Native mouse/key delivery. Diagnostics read the public cursor and geometry; only the
// explicitly named host-replacement probe writes source data through FinancialSeries.SetData.
export async function validateFinancialCursors(page,{inspect,scenario,output}){
 await mkdir(output,{recursive:true});
 const saved=()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.ExportDocument()));
 const cursor=()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.InspectFinancialCursor()));
 const frames=()=>page.evaluate(()=>new Promise(done=>requestAnimationFrame(()=>requestAnimationFrame(done))));
 async function currentFrame(){
  // ActualWidth can update before the queued compile. Require the viewport, control,
  // model revision and compiled frame to agree before deriving pointer coordinates.
  await page.waitForFunction(v=>globalThis.chartBridge.FinancialFrameIsCurrent(v.width,v.height),page.viewportSize(),{timeout:45000});
 }
 async function paintedReadout(){
  // FinancialHit is synchronous; FinancialCursorBounds describes the later paint.
  // requestAnimationFrame alone does not guarantee that Uno's overlay has painted.
  await page.waitForFunction(()=>{const s=JSON.parse(globalThis.chartBridge.InspectFinancialCursor());return s.hit&&s.readoutBounds?.width>0;});
 }
 async function settled(before){await page.waitForFunction(p=>{const s=JSON.parse(globalThis.chartBridge.Inspect());return !s.error&&s.vertices>0&&(s.backend.startsWith('OpenGL')?s.gpuSubmissions>p.gpuSubmissions:s.overlayDraws>p.overlayDraws);},before,{timeout:45000});}
 async function select(id){await page.mouse.move(2,2);const before=await inspect();await page.evaluate(id=>globalThis.chartBridge.Command('example',id),id);await settled(before);}
 async function load(d){await page.mouse.move(2,2);const before=await inspect();await page.evaluate(d=>globalThis.chartBridge.ImportDocument(JSON.stringify(d)),d);await settled(before);}
 async function fixture(dense=false){
  await select('signals');const d=await saved();d.title='Original OHLC cursor';d.showLegend=false;d.layout='Layered';
  d.xAxes[0].minimum=0;d.xAxes[0].maximum=10;d.yAxes[0].minimum=0;d.yAxes[0].maximum=20;
  d.xAxes[0].tickOptions={mode:'Automatic',numericFormat:'F2',dateTimeFormat:'',ticks:[]};
  d.yAxes[0].tickOptions={mode:'Automatic',numericFormat:'F2',dateTimeFormat:'',ticks:[]};
  const candles=[[4,10,16,8,12,12345]];
  if(dense)for(let i=1;i<12000;i++)candles.push([8+((i*7919)%11999)/12000,2,4,1,3,i]);
  else candles.push([8,2,4,1,3,50]);
  d.series=[{kind:'financial',title:'Original OHLC',visible:true,cursorTrackEnabled:true,color:[1,0,0,1],xAxis:'x',yAxis:'y',strokeWidth:1.5,style:'Candlestick',cursorField:'Close',candles}];
  await load(d);return d;
 }
 async function sourcePoint(field='Close',series=0,index=0){
  await currentFrame();
  return page.evaluate(([s,i,f])=>{
   const chart=JSON.parse(globalThis.chartBridge.Inspect());
   const p=JSON.parse(globalThis.chartBridge.FinancialSourceScreen(s,i,f));
   return{x:chart.bounds.x+p.x,y:chart.bounds.y+p.y};
  },[series,index,field]);
 }
 async function hover(field='Close',title='Original OHLC'){
  // Independent mapping for the default fixture. Bound secondary/stacked axes use a
  // read-only host projection probe in their separate scenario below.
  await currentFrame();const s=await inspect();const y={Open:10,High:16,Low:8,Close:12}[field];
  const p={x:s.bounds.x+s.plot.x+.4*s.plot.width,y:s.bounds.y+s.plot.y+(1-y/20)*s.plot.height};
  await page.mouse.move(p.x,p.y);
  await page.waitForFunction(([field,title])=>{const s=JSON.parse(globalThis.chartBridge.InspectFinancialCursor());return s.hit?.index===0&&s.hit.field===field&&s.hit.title===title&&s.readoutBounds?.width>0;},[field,title]);
  await frames();return cursor();
 }
 async function stable(read,label){
  const end=Date.now()+10000;let old=null,since=0,last;
  while(Date.now()<end){await frames();last=await read();const v=page.viewportSize();const visible=last&&last.width>1&&last.height>1&&last.x>=0&&last.y>=0&&last.x+last.width<=v.width+1&&last.y+last.height<=v.height+1;
   const same=visible&&old&&['x','y','width','height'].every(k=>Math.abs(last[k]-old[k])<.1);if(!same)since=Date.now();if(same&&Date.now()-since>=180)return last;old=visible?last:null;}
  throw new Error(label+' not actionable: '+JSON.stringify(last));
 }
 async function property(name){
  const end=Date.now()+20000;let previous=null,since=0,nextWheel=0,last;
  while(Date.now()<end){await frames();last=await page.evaluate(name=>({b:JSON.parse(globalThis.chartBridge.PropertyBounds('Series',name)),v:JSON.parse(globalThis.chartBridge.PropertyViewportBounds('Series',name))}),name);
   const {b,v}=last;const visible=b.width>1&&b.height>1&&b.x>=v.x-1&&b.x+b.width<=v.x+v.width+1&&b.y>=v.y&&b.y+b.height<=v.y+v.height+1;
   const same=visible&&previous&&['x','y','width','height'].every(k=>Math.abs(b[k]-previous[k])<.1);if(!same)since=Date.now();if(same&&Date.now()-since>=200)return b;previous=visible?b:null;
   if(!visible&&Date.now()>=nextWheel){await page.mouse.move(v.x+3,v.y+v.height/2);await page.mouse.wheel(0,Math.max(-550,Math.min(550,b.y+b.height/2-v.y-v.height/2)));nextWheel=Date.now()+250;}
  }
  throw new Error('Financial property outside viewport '+name+': '+JSON.stringify(last));
 }
 async function chooseField(index,field){
  const b=await property('CursorField');await page.mouse.click(b.x+b.width/2,b.y+b.height/2);
  const item=await stable(()=>page.evaluate(i=>{const b=globalThis.chartBridge.PropertyOptionBounds('Series','CursorField',i);return b?JSON.parse(b):null;},index),'Financial field option');
  await page.mouse.click(item.x+item.width/2,item.y+item.height/2);
  await page.waitForFunction(f=>JSON.parse(globalThis.chartBridge.ExportDocument()).series[0].cursorField===f,field);
 }
 async function tracking(value){const b=await property('CursorTrackEnabled');await page.mouse.click(b.x+12,b.y+b.height/2);await page.waitForFunction(v=>JSON.parse(globalThis.chartBridge.ExportDocument()).series[0].cursorTrackEnabled===v,value);}
 async function download(title,name){
  await page.evaluate(t=>globalThis.chartBridge.BringActionIntoView(t),title);
  const b=await stable(()=>page.evaluate(t=>JSON.parse(globalThis.chartBridge.ActionBounds(t)),title),title);
  const pending=page.waitForEvent('download');await page.mouse.click(b.x+b.width/2,b.y+b.height/2);const file=await pending;const path=resolve(output,name);await file.saveAs(path);return readFile(path);
 }
 await scenario('financial: native pointer shows complete original OHLC and visible result table',async()=>{
  await fixture();const before=PNG.sync.read(await page.screenshot({scale:'css'}));const result=await hover();
  assert.deepEqual(result.hit.sample,{x:4,open:10,high:16,low:8,close:12,volume:12345});assert.equal(result.hit.data.y,12);assert(result.hit.distance<2);
  for(const text of ['Open   10.00','High   16.00','Low    8.00','Close  12.00','Volume 12345'])assert(result.readout.includes(text),text);
  const s=await inspect(),b=result.readoutBounds,after=PNG.sync.read(await page.screenshot({path:resolve(output,'financial-cursor.png'),scale:'css'}));let changed=0,textPixels=0;
  for(let y=Math.ceil(s.bounds.y+b.y+2);y<Math.floor(s.bounds.y+b.y+b.height-2);y++)for(let x=Math.ceil(s.bounds.x+b.x+2);x<Math.floor(s.bounds.x+b.x+b.width-2);x++){
   const i=(y*after.width+x)*4;if(before.data[i]!==after.data[i]||before.data[i+1]!==after.data[i+1]||before.data[i+2]!==after.data[i+2])changed++;
   if(after.data[i]>120&&after.data[i+1]>120&&after.data[i+2]>100)textPixels++;
  }
  assert(changed>1000&&textPixels>150,'Result table must contain painted text, not only metadata');return {changed,textPixels,readout:result.readout};
 });
 await scenario('financial: native field selector tracks Open High Low and Close without rewriting candles',async()=>{
  const d=await fixture();for(const [index,field] of ['Open','High','Low','Close'].entries()){await chooseField(index,field);const state=await hover(field);assert.equal(state.hit.data.y,{Open:10,High:16,Low:8,Close:12}[field]);assert.deepEqual((await saved()).series[0].candles,d.series[0].candles);}
  await page.screenshot({path:resolve(output,'financial-property-controls.png'),scale:'css'});
 });
 await scenario('financial: tracking checkbox keeps geometry and original candles visible',async()=>{
  const d=await fixture();await hover();const before=await inspect();await tracking(false);
  const p=await sourcePoint();await page.mouse.move(p.x,p.y);await frames();assert.equal((await cursor()).hit,null);assert.equal((await cursor()).readout,'');
  const s=await inspect();assert.equal(s.vertices,before.vertices);const data=(await saved()).series[0];assert(data.visible);assert.deepEqual(data.candles,d.series[0].candles);
  await tracking(true);assert((await hover()).hit);
 });
 await scenario('financial: dense unordered cursor reuses index text and GPU geometry on movement',async()=>{
  await fixture(true);const initial=await hover();assert.equal(initial.indexBuilds,1);assert(initial.testedSamples<512);
  const before=await inspect(),stats=await page.evaluate(()=>globalThis.FastChartsSceneWebGL.inspect()),p=await sourcePoint();
  for(let i=1;i<=6;i++)await page.mouse.move(p.x+i*.6,p.y);await frames();
  const after=await inspect(),result=await cursor(),next=await page.evaluate(()=>globalThis.FastChartsSceneWebGL.inspect());
  assert.equal(result.hit.index,0);assert.equal(result.indexBuilds,1);assert.equal(result.textFormats,initial.textFormats);assert.equal(after.gpuSubmissions,before.gpuSubmissions);assert.deepEqual(next.map(s=>s.uploads),stats.map(s=>s.uploads));return result;
 });
 await scenario('financial: tied series and line overlays resolve in draw order with tracking opt-out',async()=>{
  const d=await fixture();d.series[1]=structuredClone(d.series[0]);d.series[1].title='Top candle';
  d.series.push({kind:'line',title:'Close overlay',visible:true,cursorTrackEnabled:true,color:[0,1,1,1],xAxis:'x',yAxis:'y',strokeWidth:2,interpolation:'Linear',marker:'Circle',markerSize:5,showLine:true,fillToBaseline:false,baseline:0,data:{kind:'xy',x:[4],y:[12]}});await load(d);
  let p=await sourcePoint();await page.mouse.move(p.x,p.y);await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectSampleCursor()).hit?.title==='Close overlay');assert.equal((await cursor()).hit,null);
  d.series[2].cursorTrackEnabled=false;await load(d);assert.equal((await hover('Close','Top candle')).hit.index,0);
  d.series[1].visible=false;await load(d);assert.equal((await hover()).hit.title,'Original OHLC');
 });
 await scenario('financial: inverted logarithmic secondary axes confine readout to the bound pane',async()=>{
  const d=await fixture();d.layout='Stacked';d.xAxes.push({...d.xAxes[0],id:'financial-x',minimum:.1,maximum:10,scale:'Logarithmic',inverted:true});
  d.yAxes.push({...d.yAxes[0],id:'financial-y',minimum:1,maximum:20,scale:'Logarithmic',inverted:true});d.xAxes[0].minimum=100;d.xAxes[0].maximum=200;d.yAxes[0].minimum=100;d.yAxes[0].maximum=200;
  d.series[0].xAxis='financial-x';d.series[0].yAxis='financial-y';await load(d);const p=await sourcePoint();await page.mouse.move(p.x,p.y);
  await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectFinancialCursor()).hit?.index===0);await paintedReadout();const state=await cursor(),b=state.readoutBounds,pane=state.hit.paneBounds;
  assert.equal(state.hit.sample.close,12);assert(b.x>=pane.x&&b.y>=pane.y&&b.x+b.width<=pane.x+pane.width&&b.y+b.height<=pane.y+pane.height);
  const chart=await inspect();await page.mouse.move(p.x,chart.bounds.y+chart.plot.y+8);await frames();assert.equal((await cursor()).hit,null);return {pane,readout:b};
 });
 await scenario('financial: same-instance host replacement refreshes exact volume and source index',async()=>{
  await fixture(true);const before=await hover();await page.evaluate(()=>globalThis.chartBridge.CheckFinancialHostReplacement());
  await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectFinancialCursor()).hit?.sample.volume===12346);const after=await cursor();assert.equal(after.indexBuilds,before.indexBuilds+1);assert(after.readout.includes('Volume 12346'));assert.equal(after.hit.seriesId,before.hit.seriesId);
  // A host callback can mutate the same document while other cursor events are being
  // delivered. Never publish an OHLC record from the pre-callback document revision.
  const d=await fixture();d.series.unshift({kind:'heatmap',title:'Background cells',visible:true,cursorTrackEnabled:true,color:[1,1,1,1],xAxis:'x',yAxis:'y',strokeWidth:1,
   grid:{columns:1,rows:1,xMinimum:0,xMaximum:10,yMinimum:0,yMaximum:20,values:[1]}});await load(d);
  await page.evaluate(()=>globalThis.chartBridge.ArmFinancialNotificationReview());
  try{
   await hover();await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectFinancialCursor()).hit?.sample.volume===12346);
   assert.equal(await page.evaluate(()=>globalThis.chartBridge.FinishFinancialNotificationReview()),'passed');
  }finally{await page.evaluate(()=>globalThis.chartBridge.CancelFinancialNotificationReview());}
 });
 await scenario('financial: toolbar downloads persist cursor settings and complete original data',async()=>{
  const d=await fixture();await chooseField(1,'High');await hover('High');const expected=await saved();
  const document=JSON.parse((await download('Save chart','financial.fcscene.json')).toString());assert.deepEqual(document,expected);assert.equal(document.series[0].cursorField,'High');assert.deepEqual(document.series[0].candles,d.series[0].candles);
  const csv=(await download('All data CSV','financial-original.csv')).toString();assert(csv.includes('Open,High,Low,Close,Volume')&&csv.includes('12345'));
  await load(document);assert.deepEqual(await saved(),document);assert((await hover('High')).hit);
 });
 await scenario('financial: compact viewport contains the readout and exit replacement clear public state',async()=>{
  const d=await fixture();await page.setViewportSize({width:480,height:800});await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.Inspect()).bounds.width<=480);
  const p=await sourcePoint();await page.mouse.move(p.x,p.y);await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.InspectFinancialCursor()).hit?.index===0);await paintedReadout();
  const s=await cursor(),b=s.readoutBounds,pane=s.hit.paneBounds;assert(b.x>=pane.x&&b.y>=pane.y&&b.x+b.width<=pane.x+pane.width&&b.y+b.height<=pane.y+pane.height);
  await page.screenshot({path:resolve(output,'financial-mobile-cursor.png'),scale:'css'});await page.mouse.move(2,2);await page.waitForFunction(()=>{const s=JSON.parse(globalThis.chartBridge.InspectFinancialCursor());return s.hit===null&&s.readout===''&&s.readoutBounds===null;});
  await page.setViewportSize({width:1440,height:960});await load(d);assert.equal((await cursor()).hit,null);await hover();await select('surface');assert.equal((await cursor()).hit,null);
 });
 await validateFinancialX(page,{inspect,scenario,output,fixture,load,select,hover,cursor,saved,currentFrame,frames,property,stable,sourcePoint,paintedReadout,chooseField,download});
 await validateCursorTable(page,{inspect,scenario,output,fixture,load,select,hover,cursor,saved,currentFrame,frames,property,stable,sourcePoint,paintedReadout,chooseField,download});
 await select('signals');
}
