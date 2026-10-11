// Independent WebGL2 backend. No global WebGL/runtime patch; borrowed memory views never escape a call.
(()=>{
'use strict';
if(globalThis.FastChartsSceneWebGL) return;
const contexts=new Map();
const maxVertexBytes=256*1024*1024;
const vertexSource=`#version 300 es
precision highp float;
layout(location=0) in vec3 aPosition;
layout(location=1) in vec4 aColor;
layout(location=2) in vec3 aNormal;
uniform mat4 uMatrix;
uniform float uPointSize;
out vec4 vColor;
void main(){
 gl_Position=uMatrix*vec4(aPosition,1.0); gl_PointSize=uPointSize;
 float light=1.0;
 if(dot(aNormal,aNormal)>0.001) light=0.35+0.65*abs(dot(normalize(aNormal),normalize(vec3(0.4,0.8,0.6))));
 vColor=vec4(aColor.rgb*light,aColor.a);
}`;
const fragmentSource=`#version 300 es
precision highp float;
in vec4 vColor;
out vec4 fragmentColor;
void main(){if(vColor.a<=0.0)discard;fragmentColor=vColor;}`;
function shader(gl,type,text){
 const result=gl.createShader(type);if(!result)throw new Error('Could not allocate shader');
 gl.shaderSource(result,text);gl.compileShader(result);
 if(!gl.getShaderParameter(result,gl.COMPILE_STATUS)){const error=gl.getShaderInfoLog(result);gl.deleteShader(result);throw new Error(error);}
 return result;
}
function initialize(state){
 const gl=state.gl;const vertex=shader(gl,gl.VERTEX_SHADER,vertexSource);let fragment,program,vao,buffer;
 try{
  fragment=shader(gl,gl.FRAGMENT_SHADER,fragmentSource);program=gl.createProgram();
  if(!program)throw new Error('Could not allocate shader program');
  gl.attachShader(program,vertex);gl.attachShader(program,fragment);gl.linkProgram(program);
  if(!gl.getProgramParameter(program,gl.LINK_STATUS))throw new Error(gl.getProgramInfoLog(program));
  vao=gl.createVertexArray();buffer=gl.createBuffer();if(!vao||!buffer)throw new Error('Could not allocate vertex resources');
  gl.bindVertexArray(vao);gl.bindBuffer(gl.ARRAY_BUFFER,buffer);
  gl.enableVertexAttribArray(0);gl.vertexAttribPointer(0,3,gl.FLOAT,false,40,0);
  gl.enableVertexAttribArray(1);gl.vertexAttribPointer(1,4,gl.FLOAT,false,40,12);
  gl.enableVertexAttribArray(2);gl.vertexAttribPointer(2,3,gl.FLOAT,false,40,28);
  state.program=program;state.vao=vao;state.buffer=buffer;
  state.matrix=gl.getUniformLocation(program,'uMatrix');state.pointSize=gl.getUniformLocation(program,'uPointSize');
  state.capacity=0;state.uploadedBytes=0;state.metadata=null;state.frame=null;state.status=1;state.error='';state.generation++;
 }catch(error){if(buffer)gl.deleteBuffer(buffer);if(vao)gl.deleteVertexArray(vao);if(program)gl.deleteProgram(program);throw error;}
 finally{gl.deleteShader(vertex);if(fragment)gl.deleteShader(fragment);}
}
function create(id,notify){
 if(contexts.has(id))throw new Error('Duplicate chart context');
 const canvas=document.createElement('canvas');
 const gl=canvas.getContext('webgl2',{alpha:true,premultipliedAlpha:true,depth:true,stencil:false,antialias:false,preserveDrawingBuffer:false,powerPreference:'high-performance'});
 if(!gl)throw new Error('WebGL2 is unavailable; select the Skia fallback.');
 const state={id,canvas,gl,notify,readback:null,flipped:null,staging:null,status:0,error:'',disposed:false,
  generation:0,uploads:0,bufferAllocations:0,stagingAllocations:0,readbackAllocations:0};
 state.onLost=event=>{
  event.preventDefault();if(state.disposed)return;
  state.status=0;state.uploadedBytes=0;state.notify?.(0);
 };
 state.onRestored=()=>{
  if(state.disposed)return;
  try{initialize(state);state.notify?.(1);}
  catch(error){state.status=-1;state.error=String(error);state.notify?.(-1);}
 };
 canvas.addEventListener('webglcontextlost',state.onLost);canvas.addEventListener('webglcontextrestored',state.onRestored);
 try{initialize(state);contexts.set(id,state);}
 catch(error){state.disposed=true;state.notify=null;canvas.removeEventListener('webglcontextlost',state.onLost);canvas.removeEventListener('webglcontextrestored',state.onRestored);gl.getExtension('WEBGL_lose_context')?.loseContext();throw error;}
 const debug=gl.getExtension('WEBGL_debug_renderer_info');
 return 'WebGL2 · '+(debug?gl.getParameter(debug.UNMASKED_RENDERER_WEBGL):gl.getParameter(gl.RENDERER));
}
function draw(id,vertices,metadata,width,height,pixels){
 const state=contexts.get(id);if(!state)throw new Error('Chart context is not initialized');
 const {gl,canvas}=state;if(state.status!==1||gl.isContextLost())throw new Error(state.error||'WebGL context lost; waiting for restoration.');
 if(!Number.isInteger(width)||!Number.isInteger(height)||width<1||height<1||width*height>16000000)throw new Error('Invalid chart framebuffer size');
 if(pixels.length!==width*height*4)throw new Error('RGBA destination length mismatch');
 if(metadata!==state.metadata){state.frame=JSON.parse(metadata);state.metadata=metadata;}
 const frame=state.frame;
 const required=frame.vertexCount*40;
 if(!Number.isSafeInteger(required)||required<0||required>maxVertexBytes)throw new Error('Chart vertex data exceeds the 256 MiB limit');
 if(vertices.length!==0&&vertices.length!==required)throw new Error('Packed vertex length mismatch');
 if(canvas.width!==width||canvas.height!==height){canvas.width=width;canvas.height=height;}
 if(!state.readback||state.readback.length!==pixels.length){state.readback=new Uint8Array(pixels.length);state.flipped=new Uint8Array(pixels.length);state.readbackAllocations++;}
 gl.bindFramebuffer(gl.FRAMEBUFFER,null);gl.viewport(0,0,width,height);gl.disable(gl.SCISSOR_TEST);gl.disable(gl.CULL_FACE);
 gl.colorMask(true,true,true,true);gl.depthMask(true);gl.clearDepth(1);
 const hex=frame.background.replace('#',''),alpha=hex.length===8?parseInt(hex.slice(6,8),16)/255:1;
 gl.clearColor(parseInt(hex.slice(0,2),16)/255*alpha,parseInt(hex.slice(2,4),16)/255*alpha,parseInt(hex.slice(4,6),16)/255*alpha,alpha);
 gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);gl.useProgram(state.program);gl.bindVertexArray(state.vao);gl.bindBuffer(gl.ARRAY_BUFFER,state.buffer);
 if(vertices.length>0){
  if(!state.staging||state.staging.length<required){let capacity=4096;while(capacity<required)capacity*=2;state.staging=new Uint8Array(capacity);state.stagingAllocations++;}
  if(state.capacity<required){gl.bufferData(gl.ARRAY_BUFFER,state.staging.length,gl.DYNAMIC_DRAW);state.capacity=state.staging.length;state.bufferAllocations++;}
  vertices.copyTo(state.staging.subarray(0,required));gl.bufferSubData(gl.ARRAY_BUFFER,0,state.staging,0,required);
  state.uploadedBytes=required;state.uploads++;
 }
 if(required>state.uploadedBytes)throw new Error('Vertex upload required after context restoration');
 gl.enable(gl.BLEND);gl.blendEquationSeparate(gl.FUNC_ADD,gl.FUNC_ADD);gl.blendFuncSeparate(gl.SRC_ALPHA,gl.ONE_MINUS_SRC_ALPHA,gl.ONE,gl.ONE_MINUS_SRC_ALPHA);
 gl.enable(gl.SCISSOR_TEST);gl.depthFunc(gl.LEQUAL);
 const sx=width/frame.width,sy=height/frame.height;
 for(const batch of frame.batches){
  if(batch.depthTest)gl.enable(gl.DEPTH_TEST);else gl.disable(gl.DEPTH_TEST);gl.depthMask(batch.depthTest);
  const clip=batch.clip,left=Math.floor(clip.x*sx),bottom=Math.floor((frame.height-clip.y-clip.height)*sy);
  gl.scissor(left,bottom,Math.max(0,Math.ceil((clip.x+clip.width)*sx)-left),Math.max(0,Math.ceil((frame.height-clip.y)*sy)-bottom));
  gl.uniformMatrix4fv(state.matrix,false,batch.transform);gl.uniform1f(state.pointSize,batch.pointSize*sx);
  gl.drawArrays(batch.kind===1?gl.POINTS:gl.TRIANGLES,batch.start,batch.count);
 }
 gl.pixelStorei(gl.PACK_ALIGNMENT,1);gl.readPixels(0,0,width,height,gl.RGBA,gl.UNSIGNED_BYTE,state.readback);
 const error=gl.getError();if(error!==gl.NO_ERROR)throw new Error('WebGL rendering failed with error 0x'+error.toString(16));
 const stride=width*4;
 for(let y=0;y<height;y++)state.flipped.set(state.readback.subarray((height-1-y)*stride,(height-y)*stride),y*stride);
 pixels.set(state.flipped);
}
function destroy(id){
 const state=contexts.get(id);if(!state)return;contexts.delete(id);const {gl,canvas}=state;
 state.disposed=true;state.notify=null;canvas.removeEventListener('webglcontextlost',state.onLost);canvas.removeEventListener('webglcontextrestored',state.onRestored);
 if(!gl.isContextLost()){gl.deleteBuffer(state.buffer);gl.deleteVertexArray(state.vao);gl.deleteProgram(state.program);gl.getExtension('WEBGL_lose_context')?.loseContext();}
 state.readback=state.flipped=state.staging=state.frame=state.metadata=null;
}
// Read-only counters expose retained allocation and context generation, not chart data or GL objects.
function inspect(){return [...contexts.values()].map(s=>({id:s.id,status:s.status,generation:s.generation,uploads:s.uploads,
 bufferAllocations:s.bufferAllocations,stagingAllocations:s.stagingAllocations,readbackAllocations:s.readbackAllocations,capacity:s.capacity,uploadedBytes:s.uploadedBytes}));}
globalThis.FastChartsSceneWebGL=Object.freeze({create,draw,destroy,inspect});
})();
