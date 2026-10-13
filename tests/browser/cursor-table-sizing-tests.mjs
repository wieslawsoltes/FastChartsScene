import assert from 'node:assert/strict';
import {resolve} from 'node:path';

export async function validateCursorTableSizing(page,h){
 const {inspect,scenario,output,setup,sample,action,state,currentFrame,stable}=h;
 const bounds=()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.CursorTableBounds('$table')));
 const document=()=>page.evaluate(()=>globalThis.chartBridge.ExportDocument());
 async function height(value){
  await page.waitForFunction(value=>Math.abs(JSON.parse(globalThis.chartBridge.CursorTableBounds('$table')).height-value)<1,value);
  await currentFrame();return bounds();
 }
 async function grip(){return stable(()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.CursorTableBounds('Resize cursor table'))),'resize grip');}
 async function toolbar(title){
  await page.evaluate(t=>globalThis.chartBridge.BringActionIntoView(t),title);
  const b=await stable(()=>page.evaluate(t=>JSON.parse(globalThis.chartBridge.ActionBounds(t)),title),title);
  await page.mouse.click(b.x+b.width/2,b.y+b.height/2);
 }
 await scenario('cursor table sizing: native drag resizes the pane without changing chart values or row objects',async()=>{
  assert.equal(await page.evaluate(()=>globalThis.chartBridge.CheckCursorRowFormattingReuse()),'passed');
  await setup();const values=await sample(),saved=await document(),before=await bounds(),chart=await inspect(),g=await grip();
  await page.mouse.move(g.x+g.width/2,g.y+g.height/2);await page.mouse.down();
  await page.mouse.move(g.x+g.width/2,g.y+g.height/2-72,{steps:8});await page.mouse.up();
  const after=await height(before.height+72);assert(Math.abs((await inspect()).bounds.height-chart.bounds.height+72)<1);
  assert.equal(await document(),saved);assert.equal((await state()).created,values.created);assert.equal((await state()).updated,values.updated);
  assert.deepEqual((await state()).values,values.values);
  await page.screenshot({path:resolve(output,'cursor-table-resized.png'),scale:'css'});
  return {before:before.height,after:after.height,chartHeight:(await inspect()).bounds.height};
 });
 await scenario('cursor table sizing: focused grip supports keyboard adjustment and reset',async()=>{
  await setup();await sample();await action('Resize cursor table');await page.keyboard.press('Home');await height(210);
  await page.keyboard.press('ArrowUp');await height(222);await page.keyboard.press('ArrowDown');await height(210);
  await page.keyboard.press('PageUp');await height(258);await page.keyboard.press('PageDown');await height(210);
  // The reset key belongs to the divider, not the chart's Home-to-fit shortcut.
  const saved=await document();await page.keyboard.press('Home');await height(210);assert.equal(await document(),saved);
  await page.keyboard.press('PageUp');await height(258);const g=await grip();
  await page.mouse.dblclick(g.x+g.width/2,g.y+g.height/2);await height(210);assert.equal(await document(),saved);
 });
 await scenario('cursor table sizing: bounded preferred height survives short windows and Details toggles',async()=>{
  await setup();await sample();const saved=await document();await action('Resize cursor table');
  for(let i=0;i<12;i++)await page.keyboard.press('PageUp');await height(520);assert((await inspect()).bounds.height>=240);
  await toolbar('Details');
  await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.CursorTableBounds('$table')).height<520&&JSON.parse(globalThis.chartBridge.Inspect()).bounds.height>=239);
  await currentFrame();
  assert((await bounds()).height<520);assert((await inspect()).bounds.height>=239);
  await toolbar('Details');await height(520);
  await page.setViewportSize({width:480,height:500});await currentFrame();
  const small=await bounds(),chart=(await inspect()).bounds;
  assert(small.height<520&&small.height>60);assert(chart.height>=(chart.height+small.height)*.54);
  assert(small.y+small.height<=500&&chart.width<=480);assert.equal(await document(),saved);
  await page.screenshot({path:resolve(output,'cursor-table-short-window.png'),scale:'css'});
  await page.setViewportSize({width:1440,height:960});await height(520);
  await action('Resize cursor table');await page.keyboard.press('Home');await height(210);await action('Cursor table');
  return {compactTableHeight:small.height,compactChartHeight:chart.height};
 });
}
