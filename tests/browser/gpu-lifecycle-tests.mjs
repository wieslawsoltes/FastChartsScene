import assert from 'node:assert/strict';

// Test-only observation: retain the real chart context so the standard Khronos loss extension
// can exercise real browser/managed recovery. Production code never replaces browser methods.
export async function observeChartContexts(page){
 await page.addInitScript(()=>{
  const original=HTMLCanvasElement.prototype.getContext;
  globalThis.observedChartContexts=[];
  HTMLCanvasElement.prototype.getContext=function(type,options,...rest){
   const context=original.call(this,type,options,...rest);
   if(type==='webgl2'&&context&&options?.powerPreference==='high-performance'&&options?.stencil===false&&options?.antialias===false)
    if(!globalThis.observedChartContexts.includes(context))globalThis.observedChartContexts.push(context);
   return context;
  };
 });
}
export async function validateGpuLifecycle(page,{inspect,select,scenario,image}){
 const stats=()=>page.evaluate(()=>globalThis.FastChartsSceneWebGL.inspect());
 async function ready(previous){
  await page.waitForFunction(n=>{
   const state=JSON.parse(globalThis.chartBridge.Inspect());
   return state.backend.startsWith('OpenGL')&&state.gpuSubmissions>n&&!state.error;
  },previous.gpuSubmissions);
 }
 await scenario('GPU retains upload buffers across same-size chart edits',async()=>{
  await select('signals');const before=(await stats())[0];
  for(let i=0;i<8;i++){
   const previous=await inspect();await page.evaluate(t=>globalThis.chartBridge.Command('theme',t),i%2===0?'light':'dark');await ready(previous);
  }
  const states=await stats();assert.equal(states.length,1);const after=states[0];
  assert.equal(after.bufferAllocations,before.bufferAllocations);assert.equal(after.stagingAllocations,before.stagingAllocations);
  assert.equal(after.readbackAllocations,before.readbackAllocations);assert(after.uploads>=before.uploads+8);
  const chart=await inspect();await page.mouse.move(chart.bounds.x+chart.plot.x+40,chart.bounds.y+chart.plot.y+50);
  await page.evaluate(()=>new Promise(done=>requestAnimationFrame(()=>requestAnimationFrame(done))));
  assert.equal((await stats())[0].uploads,after.uploads,'Cursor-only overlay must not upload geometry');
  return {before,after};
 });
 for(const changedWhileLost of [false,true])await scenario('GPU restores '+(changedWhileLost?'latest edited document':'unchanged cached document')+' after real context loss',async()=>{
  await select('surface');const original=await inspect(),start=(await stats())[0];
  await page.evaluate(()=>{
   const context=globalThis.observedChartContexts.find(c=>!c.canvas.isConnected&&!c.isContextLost());
   if(!context)throw new Error('No observed chart WebGL context');
   globalThis.chartLossExtension=context.getExtension('WEBGL_lose_context');
   if(!globalThis.chartLossExtension)throw new Error('Real context loss extension is unavailable');
   globalThis.chartLossExtension.loseContext();
  });
  await page.waitForFunction(before=>{
   const state=JSON.parse(globalThis.chartBridge.Inspect());return state.backend==='Skia fallback'&&state.overlayDraws>before.overlayDraws;
  },original);
  if(changedWhileLost){
   const before=await inspect();await page.evaluate(()=>globalThis.chartBridge.Command('example','heatmap'));
   await page.waitForFunction(n=>{const s=JSON.parse(globalThis.chartBridge.Inspect());return s.example==='heatmap'&&s.vertices>0&&s.overlayDraws>n;},before.overlayDraws);
  }
  const saved=await page.evaluate(()=>globalThis.chartBridge.ExportDocument());await image('lost-context-'+changedWhileLost);
  await page.evaluate(()=>globalThis.chartLossExtension.restoreContext());await ready(original);
  await page.waitForFunction(g=>globalThis.FastChartsSceneWebGL.inspect()[0]?.generation>g,start.generation);
  const state=await inspect(),end=(await stats())[0];
  assert.equal(await page.evaluate(()=>globalThis.chartBridge.ExportDocument()),saved);
  assert(end.uploads>start.uploads);assert.equal(end.generation,start.generation+1);assert.equal(end.bufferAllocations,start.bufferAllocations+1);
  assert.equal(end.stagingAllocations,start.stagingAllocations,'Restoration reuses owned CPU staging storage');
  assert.equal(state.example,changedWhileLost?'heatmap':'surface');assert.equal(state.error,null);await image('restored-context-'+changedWhileLost);
  return {start,end,example:state.example};
 });
 await scenario('destroyed lost GPU contexts release callbacks and resources',async()=>{
  await page.evaluate(()=>{
   globalThis.releaseNotifications=0;
   globalThis.FastChartsSceneWebGL.create(1000000,()=>globalThis.releaseNotifications++);
   const context=globalThis.observedChartContexts.at(-1);globalThis.releaseExtension=context.getExtension('WEBGL_lose_context');
   if(!globalThis.releaseExtension)throw new Error('Missing context loss extension');globalThis.releaseExtension.loseContext();
  });
  await page.waitForFunction(()=>globalThis.releaseNotifications===1);
  await page.evaluate(()=>{globalThis.FastChartsSceneWebGL.destroy(1000000);globalThis.releaseExtension.restoreContext();});
  await page.waitForTimeout(150);
  assert.equal(await page.evaluate(()=>globalThis.releaseNotifications),1);assert.equal((await stats()).length,1);
  const before=await inspect();await select('signals');assert((await inspect()).gpuSubmissions>before.gpuSubmissions);
 });
}
