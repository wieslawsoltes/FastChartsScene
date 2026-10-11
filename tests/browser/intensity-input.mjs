// Actionability checks for real Uno browser input. These helpers never set model or control values.
export function intensityInput(page,state){
 async function bounds(name){
  await page.evaluate(n=>globalThis.chartBridge.BringIntensityControlIntoView(n),name);
  await page.evaluate(()=>new Promise(done=>requestAnimationFrame(()=>requestAnimationFrame(done))));
  return page.evaluate(n=>JSON.parse(globalThis.chartBridge.IntensityControlBounds(n)),name);
 }
 async function click(name){const b=await bounds(name);await page.mouse.click(b.x+b.width/2,b.y+b.height/2);}
 async function stableBounds(read,label){
  let previous=null,stableSince=0,last=null;
  const deadline=Date.now()+10000;
  while(Date.now()<deadline){
   await page.evaluate(()=>new Promise(requestAnimationFrame));
   last=await read();
   const visible=last&&last.width>1&&last.height>1&&last.x>=0&&last.y>=0&&last.x+last.width<=page.viewportSize().width+1&&last.y+last.height<=page.viewportSize().height+1;
   const same=visible&&previous&&['x','y','width','height'].every(k=>Math.abs(last[k]-previous[k])<.1);
   if(!same)stableSince=Date.now();
   if(same&&Date.now()-stableSince>=200)return last;
   previous=visible?last:null;
  }
  throw new Error(label+' did not stabilize: '+JSON.stringify(last));
 }
 async function selectOption(name,index){
  await bounds(name);
  const b=await stableBounds(()=>page.evaluate(n=>JSON.parse(globalThis.chartBridge.IntensityControlBounds(n)),name),'Intensity combo '+name);
  await page.mouse.click(b.x+b.width/2,b.y+b.height-16);
  await page.waitForFunction(n=>globalThis.chartBridge.IntensityDropDownIsOpen(n),name);
  // Popup placement/animation may move already arranged item containers. Require stable
  // screen bounds before actual pointer input; never assign selection or model properties.
  const item=await stableBounds(()=>page.evaluate(([n,i])=>{
   const raw=globalThis.chartBridge.IntensityOptionBounds(n,i);return raw?JSON.parse(raw):null;
  },[name,index]),'Intensity option '+name+' '+index);
  console.log('INTENSITY OPTION',JSON.stringify({name,index,combo:b,item}));
  await page.mouse.click(item.x+item.width/2,item.y+item.height/2);
  try{await page.waitForFunction(n=>!globalThis.chartBridge.IntensityDropDownIsOpen(n),name,{timeout:5000});}
  catch(error){console.error('INTENSITY OPTION FAILED',JSON.stringify({name,index,item,current:await page.evaluate(([n,i])=>globalThis.chartBridge.IntensityOptionBounds(n,i),[name,index]),state:await state()}));throw error;}
 }
 async function input(name,value){
  const b=await bounds(name);await page.mouse.click(b.x+b.width/2,b.y+b.height-18);
  await page.waitForFunction(()=>['INPUT','TEXTAREA'].includes(document.activeElement?.tagName));
  await page.keyboard.press('Control+a');
  const lines=value.replace(/\r\n|\r/g,'\n').split('\n');
  for(let i=0;i<lines.length;i++){if(i>0)await page.keyboard.press('Enter');await page.keyboard.insertText(lines[i]);}
  await page.keyboard.press('Tab');
  // NumberBox commits its Value through queued focus processing. Wait for the actual
  // managed value after Tab, rather than mistaking the previous value for lost input.
  await page.waitForFunction(([n,expected])=>{
   const normalize=s=>s.replace(/\r\n|\r/g,'\n');
   return normalize(globalThis.chartBridge.IntensityControlText(n))===normalize(expected);
  },[name,value],{timeout:5000});
  const delivered=await page.evaluate(n=>globalThis.chartBridge.IntensityControlText(n),name);
  console.log('INTENSITY INPUT',JSON.stringify({name,expected:value,delivered}));
  if(delivered.replace(/\r\n|\r/g,'\n')!==value.replace(/\r\n|\r/g,'\n'))throw new Error('Input delivery mismatch for '+name+': '+JSON.stringify({expected:value,delivered}));
 }
 return {click,selectOption,input};
}
