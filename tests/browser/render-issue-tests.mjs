import assert from 'node:assert/strict';
import {resolve} from 'node:path';
import {PNG} from 'pngjs';
import {writeFile} from 'node:fs/promises';

// Current native chart and toolbar exports; no alternate renderer or calculated
// replacement image is used as evidence. Fixtures enter the bounded document API.
export async function validateRenderIssues(page,{prefix,backend,dpr,output,scenario,inspect,saved,waitRendered,download}){
 const original=await saved();
 const layout=()=>page.evaluate(()=>JSON.parse(globalThis.chartBridge.InspectMeasuredAxisLayout()));
 async function load(d){const before=await inspect();await page.evaluate(d=>globalThis.chartBridge.ImportDocument(JSON.stringify(d)),d);await waitRendered(before);await settled();}
 async function settled(){
  // Browser viewport resizing completes before Uno's queued page/Grid arrangement.
  // A matching old frame and old chart size alone do not prove the requested resize.
  // Observe the actual native page, document revision, viewport and stable chart bounds.
  const deadline=Date.now()+45000;let previous='',since=0,last;
  while(Date.now()<deadline){
   await page.evaluate(()=>new Promise(done=>requestAnimationFrame(()=>requestAnimationFrame(done))));
   last=await page.evaluate(()=>{
    const c=JSON.parse(globalThis.chartBridge.Inspect()),m=JSON.parse(globalThis.chartBridge.InspectMeasuredAxisLayout());
    const current=globalThis.chartBridge.FinancialFrameIsCurrent(innerWidth,innerHeight)&&!c.error&&
     c.bounds.x>=0&&c.bounds.y>=0&&c.bounds.right<=innerWidth+.01&&c.bounds.bottom<=innerHeight+.01&&
     Math.abs(c.bounds.width-m.width)<.01&&Math.abs(c.bounds.height-m.height)<.01;
    return {current,bounds:c.bounds,plot:c.plot,revision:c.revision,viewport:[innerWidth,innerHeight]};
   });
   const key=JSON.stringify(last);if(!last.current||key!==previous)since=Date.now();
   if(last.current&&key===previous&&Date.now()-since>=200)return;
   previous=key;
  }
  throw new Error('Native chart did not settle in the requested viewport: '+JSON.stringify(last));
 }
 function fixture(){
  const d=structuredClone(original);d.title='Render issue regression';d.subtitle='';d.showLegend=false;d.showGrid=false;d.annotations=[];d.axisLayout='Layered';
  d.xAxes=d.xAxes.slice(0,1);d.yAxes=d.yAxes.slice(0,1);
  for(const a of [...d.xAxes,...d.yAxes])Object.assign(a,{minimum:0,maximum:10,scale:'Linear',inverted:false,side:'Near',title:'',units:'',tickOptions:{mode:'Automatic',numericFormat:'',dateTimeFormat:'',ticks:[]}});
  d.series=[{kind:'line',title:'Review fill',visible:true,cursorTrackEnabled:true,xAxis:d.xAxes[0].id,yAxis:d.yAxes[0].id,color:[1,0,0,1],strokeWidth:0,
   data:{kind:'xy',x:[1,9],y:[8,8]},showLine:false,fillToBaseline:true,baseline:2,interpolation:'Linear',marker:'None',markerSize:5}];
  d.theme={background:[1,1,1,1],plot:[1,1,1,1],grid:[.8,.8,.8,1],text:[0,0,0,1],accent:[1,0,0,1]};return d;
 }
 function pixel(image,x,y){const i=(Math.floor(y)*image.width+Math.floor(x))*4;return [...image.data.subarray(i,i+4)];}
 function checkColor(actual,alpha){const expected=[255,255*(1-alpha),255*(1-alpha),255];actual.forEach((v,i)=>assert(Math.abs(v-expected[i])<=3,`${actual} != ${expected}`));}
 function checkLayout(m){
  assert(m.labels.length>0,'Measured layout is not active');const visible=m.labels.filter(l=>l.visible);
  for(const l of visible){const b=l.bounds;assert(b.x>=2&&b.y>=2&&b.right<=m.width-2+.001&&b.bottom<=m.height-2+.001,JSON.stringify(l));}
  for(let i=0;i<visible.length;i++)for(let j=i+1;j<visible.length;j++){
   const a=visible[i].bounds,b=visible[j].bounds;assert(a.right<=b.x||b.right<=a.x||a.bottom<=b.y||b.bottom<=a.y,'Measured labels overlap');
  }
  return visible;
 }
 for(const band of [false,true])await scenario(prefix+`: issue 5 ${band?'band':'area'} composed opacity agrees in native pixels PNG and SVG`,async()=>{
  const d=fixture();if(band)Object.assign(d.series[0],{kind:'band',data:{kind:'xy',x:[1,9],y:[2,2]},upper:{kind:'xy',x:[1,9],y:[8,8]}});
  for(const alpha of [0,.5,1]){
   d.series[0].color[3]=alpha;await load(d);const s=await inspect();
   assert(backend==='Skia'?s.backend==='Skia (explicit)':s.backend.startsWith('OpenGL'));
   const x=s.plot.x+s.plot.width*.4,y=s.plot.y+s.plot.height*.5,a=alpha*(band?.32:.25);
   const stem=`${backend}-${dpr}-${band?'band':'area'}-alpha-${alpha}`;
   const screen=PNG.sync.read(await page.screenshot({path:resolve(output,stem+'-screen.png'),scale:'css'}));
   checkColor(pixel(screen,s.bounds.x+x,s.bounds.y+y),a);
   const png=PNG.sync.read(await download('PNG',resolve(output,stem+'.png')));
   checkColor(pixel(png,x*png.width/s.bounds.width,y*png.height/s.bounds.height),a);
   const svg=(await download('SVG',resolve(output,stem+'.svg'))).toString();
   const alphas=await page.evaluate(text=>{const d=new DOMParser().parseFromString(text,'image/svg+xml');if(d.querySelector('parsererror'))throw new Error('Invalid SVG');return [...d.querySelectorAll('polygon[fill="#FF0000"]')].map(p=>Number(p.getAttribute('fill-opacity')??1));},svg);
   if(alpha===0)assert.equal(alphas.length,0);else{assert(alphas.length>0);assert(alphas.every(v=>Math.abs(v-a)<1e-7));}
   assert.deepEqual((await saved()).series[0].data,d.series[0].data);
  }
 });
 await scenario(prefix+': issue 13 long precise endpoint labels fit desktop and compact chart bounds',async()=>{
  const d=fixture();d.xAxes[0].minimum=1e12;d.xAxes[0].maximum=1e12+1;d.series[0].data={kind:'xy',x:[1e12,1e12+1],y:[2,8]};
  for(const width of [1440,480])for(const inverted of [false,true]){
   await page.setViewportSize({width,height:960});d.xAxes[0].inverted=inverted;await load(d);
   const m=await layout(),visible=checkLayout(m).filter(l=>l.horizontal&&!l.isTitle);
   for(const endpoint of [1e12,1e12+1])assert(visible.some(l=>l.value===endpoint&&Number(l.label.text)===endpoint),'Missing precise endpoint '+endpoint);
   const stem=`${backend}-${dpr}-measured-${width}-${inverted}`;
   const chart=await inspect();
   await writeFile(resolve(output,stem+'-layout.json'),JSON.stringify({layout:m,chart,viewport:page.viewportSize()},null,2));
   const screen=PNG.sync.read(await page.screenshot({path:resolve(output,stem+'.png'),scale:'css'}));
   function ink(image,b,sx,sy,ox=0,oy=0){let n=0;
    for(let y=Math.max(0,Math.floor(oy+(b.y-1)*sy));y<Math.min(image.height,Math.ceil(oy+(b.bottom+1)*sy));y++)
     for(let x=Math.max(0,Math.floor(ox+(b.x-1)*sx));x<Math.min(image.width,Math.ceil(ox+(b.right+1)*sx));x++){
      const a=(y*image.width+x)*4;if(image.data[a]<120&&image.data[a+1]<120&&image.data[a+2]<120)n++;
     }return n;
   }
   const endpoints=visible.filter(l=>l.value===1e12||l.value===1e12+1);
   for(const label of endpoints)assert(ink(screen,label.bounds,1,1,chart.bounds.x,chart.bounds.y)>4,'Endpoint not painted in native chart');
   const svg=(await download('SVG',resolve(output,stem+'.svg'))).toString();
   assert(svg.includes('>1000000000000</text>')&&svg.includes('>1000000000001</text>'));
   assert(svg.includes('textLength=')&&svg.includes('lengthAdjust="spacingAndGlyphs"'));
   const png=PNG.sync.read(await download('PNG',resolve(output,stem+'-export.png')));assert(png.width>m.width);
   for(const label of endpoints)assert(ink(png,label.bounds,png.width/m.width,png.height/m.height)>4,'Endpoint not painted in PNG');
   const svgInk=await page.evaluate(async({svg,labels})=>{
    const url=URL.createObjectURL(new Blob([svg],{type:'image/svg+xml'}));
    try{const image=new Image();image.src=url;await image.decode();const c=document.createElement('canvas');c.width=image.width;c.height=image.height;
     const ctx=c.getContext('2d');ctx.drawImage(image,0,0);const pixels=ctx.getImageData(0,0,c.width,c.height).data;
     return labels.map(l=>{const b=l.bounds;let n=0;
      for(let y=Math.max(0,Math.floor(b.y-2));y<Math.min(c.height,Math.ceil(b.bottom+2));y++)
       for(let x=Math.max(0,Math.floor(b.x-2));x<Math.min(c.width,Math.ceil(b.right+2));x++){
        const a=(y*c.width+x)*4;if(pixels[a]<120&&pixels[a+1]<120&&pixels[a+2]<120)n++;
       }return n;
     });
    }finally{URL.revokeObjectURL(url);}
   },{svg,labels:endpoints});assert(svgInk.every(n=>n>4),'Endpoint not painted when viewing exported SVG');
   assert.deepEqual((await saved()).series[0].data,d.series[0].data);
  }
  await page.setViewportSize({width:1440,height:960});await settled();
 });
 await scenario(prefix+': issue 13 secondary layered and inverted logarithmic axes retain measured labels',async()=>{
  const d=fixture();d.yAxes[0].title='Primary';
  d.yAxes.push({...structuredClone(d.yAxes[0]),id:'long-near',minimum:1e12,maximum:1e12+1,title:'Long near'});
  d.yAxes.push({...structuredClone(d.yAxes[0]),id:'long-far',minimum:1e12,maximum:1e12+1,title:'Long far',side:'Far',inverted:true});
  d.xAxes.push({...structuredClone(d.xAxes[0]),id:'log-x',minimum:1,maximum:100,scale:'Logarithmic',inverted:true,
   tickOptions:{mode:'Custom',numericFormat:'',dateTimeFormat:'',ticks:[{value:1,label:'one'},{value:10,label:'ten'},{value:100,label:'hundred'}]}});
  await load(d);const m=await layout(),visible=checkLayout(m);
  for(const id of [d.yAxes[0].id,'long-near','long-far','log-x'])assert(visible.some(l=>l.axisId===id),id);
  for(const text of ['one','ten','hundred'])assert(visible.some(l=>l.label.text===text),text);
  const svg=(await download('SVG',resolve(output,`${backend}-${dpr}-measured-secondary.svg`))).toString();assert(svg.includes('>ten</text>'));
  await page.screenshot({path:resolve(output,`${backend}-${dpr}-measured-secondary.png`),scale:'css'});
 });
 await scenario(prefix+': issue 13 crowded custom ticks keep complete data and report suppression',async()=>{
  const d=fixture();d.xAxes[0].tickOptions={mode:'Custom',numericFormat:'',dateTimeFormat:'',ticks:Array.from({length:41},(_,i)=>({value:i/4,label:`Custom coordinate ${i}`}))};
  await load(d);const m=await layout();checkLayout(m);const ticks=m.labels.filter(l=>l.horizontal&&!l.isTitle);
  assert.equal(ticks.length,41);assert(ticks.some(l=>!l.visible&&l.reason==='overlap'));
  assert(ticks.some(l=>l.visible&&l.value===0));assert(ticks.some(l=>l.visible&&l.value===10));
  assert(ticks.every(l=>l.label.text===`Custom coordinate ${l.value*4}`));assert.deepEqual((await saved()).xAxes[0].tickOptions,d.xAxes[0].tickOptions);
 });
}
