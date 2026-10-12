import assert from 'node:assert/strict';
import {resolve} from 'node:path';
import {PNG} from 'pngjs';

// Operates on the same current-build chart as the native tick-editor suite. Fixtures are
// imported through the real bounded document API; SVG/PNG are actual toolbar downloads.
export async function validateAutomaticAxes(page,{prefix,backend,dpr,output,scenario,inspect,saved,waitRendered,download}) {
 const original=await saved();
 async function load(range,scale='Linear',numericFormat='',dateTimeFormat='') {
  const d=structuredClone(original);d.title='Automatic label precision';d.subtitle='';d.showLegend=false;
  d.xAxes=d.xAxes.slice(0,1);d.xAxes[0]={...d.xAxes[0],minimum:range[0],maximum:range[1],scale,inverted:false,
   tickOptions:{mode:'Automatic',numericFormat,dateTimeFormat,ticks:[]}};
  d.series=d.series.slice(0,1);d.series[0].xAxis=d.xAxes[0].id;
  d.series[0].data={kind:'xy',x:[range[0],range[1]],y:[0,1]};
  const before=await inspect();await page.evaluate(d=>globalThis.chartBridge.ImportDocument(JSON.stringify(d)),d);await waitRendered(before);
  return d;
 }
 async function labels(name) {
  const svg=(await download('SVG',resolve(output,backend+'-'+dpr+'-'+name+'.svg'))).toString(),s=await inspect();
  return page.evaluate(([svg,y])=>{
   const doc=new DOMParser().parseFromString(svg,'image/svg+xml');
   if(doc.querySelector('parsererror'))throw new Error('Invalid exported SVG');
   return [...doc.querySelectorAll('text')].filter(t=>Math.abs(Number(t.getAttribute('y'))-y)<.001).map(t=>t.textContent);
  },[svg,s.plot.y+s.plot.height+18]);
 }
 await scenario(prefix+': automatic narrow-range labels remain distinct in the displayed chart and exports',async()=>{
  const d=await load([1e12,1e12+1]);const text=await labels('automatic-precision');
  assert(text.length>=2);assert.equal(new Set(text).size,text.length);
  for(const label of text)assert(Number(label)>=1e12&&Number(label)<=1e12+1,label);
  assert.deepEqual((await saved()).series[0].data,d.series[0].data,'Rendering changed the source data');
  const screenshot=await page.screenshot({path:resolve(output,backend+'-'+dpr+'-automatic-precision.png'),scale:'css'});
  const png=PNG.sync.read(screenshot),s=await inspect();let ink=0;
  // Actual bottom-axis text must be painted, not merely included in metadata/SVG.
  for(let y=Math.ceil(s.bounds.y+s.plot.y+s.plot.height+7);y<Math.floor(s.bounds.y+s.plot.y+s.plot.height+22);y++)
   for(let x=Math.ceil(s.bounds.x+s.plot.x);x<Math.floor(s.bounds.x+s.plot.x+s.plot.width);x++){
    const at=(y*png.width+x)*4;if(png.data[at]>95&&png.data[at+1]>95&&png.data[at+2]>95)ink++;
   }
  assert(ink>150,'Automatic labels were not visible in the screenshot');
  return {labels:text,labelPixels:ink};
 });
 await scenario(prefix+': automatic calendar labels distinguish month ticks in a multi-year range',async()=>{
  const start=Date.UTC(2024,0,1)/1000,end=Date.UTC(2027,0,1)/1000;await load([start,end],'DateTime');
  const text=await labels('automatic-calendar');assert(text.length>=3);assert.equal(new Set(text).size,text.length);
  assert(text.some(t=>/^2024-/.test(t)),'Month-level calendar context is missing');
  return {labels:text};
 });
 await scenario(prefix+': explicit repeated labels remain a user choice',async()=>{
  await load([1e12,1e12+1],'Linear',"'fixed'0,,");const expected=(await labels('explicit-precision'));
  assert(expected.length>=2);assert(expected.every(t=>t===expected[0]),'Explicit formatting must not be rewritten');
 });
}
