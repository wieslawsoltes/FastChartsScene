import assert from 'node:assert/strict';
import {resolve} from 'node:path';
import {readFile} from 'node:fs/promises';

// All editor opening, typing, Apply/Cancel and preset transitions use native input.
export async function validateCursorFormats(page,h){
 const {inspect,scenario,output,setup,sample,action,state,currentFrame,stable}=h;
 const formats=()=>page.evaluate(()=>globalThis.chartBridge.CursorNumberFormats());
 const saved=()=>page.evaluate(()=>globalThis.chartBridge.ExportDocument());
 async function click(name){
  const b=await stable(()=>page.evaluate(n=>JSON.parse(globalThis.chartBridge.CursorFormatBounds(n)),name),name);
  await page.mouse.click(b.x+b.width/2,b.y+b.height/2);
 }
 async function type(name,value){
  await click(name);await page.keyboard.press('Control+A');await page.keyboard.type(value);
 }
 async function preset(name){await click('$button');await click(name);}
 async function download(name){
  const promise=page.waitForEvent('download');await action('Export cursor values');
  const file=await promise,path=resolve(output,name);await file.saveAs(path);return readFile(path,'utf8');
 }
 await scenario('cursor format: native Apply reformats held rows without changing source details or CSV',async()=>{
  await setup();await preset('Compact cursor numbers');await sample(.4,.1);
  await action('Hold cursor values');const before=await state(),document=await saved(),details=await page.evaluate(()=>globalThis.chartBridge.CursorTableDetails());
  const csv=await download('format-before.csv');const chart=await inspect();
  await click('$button');await type('Cursor X number format','F2');await type('Cursor value number format','E3');await click('Apply cursor number format');
  await page.waitForFunction(()=>globalThis.chartBridge.CursorNumberFormats()==='F2|E3');
  const after=await state();assert(after.held);assert.deepEqual(after.values,before.values);assert.equal(after.created,before.created);
  assert.equal(after.queries,before.queries);assert.equal(after.updated,before.updated+2);
  await page.waitForFunction(()=>{const s=JSON.parse(globalThis.chartBridge.InspectCursorTable());return ['4.00','1.200E+001','5.000E+000'].every(t=>s.paintedTexts.includes(t));});
  assert.equal(await page.evaluate(()=>globalThis.chartBridge.CursorTableDetails()),details);assert.equal(await saved(),document);
  assert.equal(await download('format-after.csv'),csv);assert.equal((await inspect()).gpuSubmissions,chart.gpuSubmissions);
  await preset('Compact cursor numbers');await action('Hold cursor values');
 });
 await scenario('cursor format: invalid drafts apply atomically and native Cancel preserves prior formats',async()=>{
  await setup();await preset('Compact cursor numbers');await sample(.4,.1);const before=await state();
  await click('$button');await type('Cursor X number format','F2');await type('Cursor value number format','F999');await click('Apply cursor number format');
  await page.waitForFunction(()=>globalThis.chartBridge.CursorFormatError().length>0);
  assert.equal(await formats(),'G9|G9');assert.equal((await state()).updated,before.updated);
  await type('Cursor value number format','F3');await click('Cancel cursor number format');assert.equal(await formats(),'G9|G9');
  await preset('Scientific cursor numbers');assert.equal(await formats(),'E6|E6');
  await preset('Exact cursor numbers');assert.equal(await formats(),'R|R');
  await preset('Compact cursor numbers');assert.equal(await formats(),'G9|G9');
 });
 await scenario('cursor format: compact viewport keeps the native format editor and result table usable',async()=>{
  await setup();await page.setViewportSize({width:480,height:800});await sample(.4,.1);
  const chart=await inspect();assert(chart.bounds.height>250);
  await click('$button');
  for(const name of ['Cursor X number format','Cursor value number format','Apply cursor number format']){
   const b=await stable(()=>page.evaluate(n=>JSON.parse(globalThis.chartBridge.CursorFormatBounds(n)),name),name);
   assert(b.x>=-1&&b.x+b.width<=481&&b.y>=-1&&b.y+b.height<=801,'Native format control is clipped: '+name);
  }
  await page.screenshot({path:resolve(output,'cursor-format-mobile.png'),scale:'css'});
  await click('Exact cursor numbers');assert.equal(await formats(),'R|R');
  await preset('Compact cursor numbers');await page.setViewportSize({width:1440,height:960});await currentFrame();
 });
 await scenario('cursor format: reusable format changes retain row identities and unchanged numeric strings',async()=>{
  assert.equal(await page.evaluate(()=>globalThis.chartBridge.CheckCursorFormatContracts()),'passed');
  await setup();await sample(.4,.1);await action('Cursor table');
 });
}
