// Actual narrow-screen toolbar interactions. The test only scrolls a clipped toolbar action
// into view through the framework; selection and dialog opening still use browser input.
export async function validateCompactActions(page,inspect,scenario,image){
 async function click(title){
  await page.evaluate(t=>globalThis.chartBridge.BringActionIntoView(t),title);
  await page.evaluate(()=>new Promise(done=>requestAnimationFrame(()=>requestAnimationFrame(done))));
  const bounds=await page.evaluate(t=>JSON.parse(globalThis.chartBridge.ActionBounds(t)),title);
  const viewport=page.viewportSize();
  if(bounds.x<0||bounds.x+bounds.width>viewport.width+1)throw new Error('Action remains clipped: '+title+' '+JSON.stringify(bounds));
  await page.mouse.click(bounds.x+bounds.width/2,bounds.y+bounds.height/2);
 }
 await scenario('compact examples menu supports keyboard selection',async()=>{
  await click('Examples');await page.waitForFunction(()=>globalThis.chartBridge.CompactExamplesIsOpen());
  await page.keyboard.press('Home');await page.keyboard.press('ArrowDown');await page.keyboard.press('Enter');
  await page.waitForFunction(()=>JSON.parse(globalThis.chartBridge.Inspect()).example==='streaming');
  await image('mobile-example-picker');
 });
 await scenario('compact inspector opens and returns to the chart',async()=>{
  await click('Inspector');await page.waitForFunction(()=>globalThis.chartBridge.InspectorIsOpen());
  await page.screenshot({path:process.env.COMPACT_INSPECTOR_SCREENSHOT??'/tmp/fastchartsscene-inspector.png',scale:'css'});
  await page.keyboard.press('Escape');await page.waitForFunction(()=>!globalThis.chartBridge.InspectorIsOpen());
  const state=await inspect();if(state.error)throw new Error(state.error);
 });
}
