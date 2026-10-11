// Independent WebGL2 backend for FastChartsScene's packed frame contract.
// This file is embedded in the reusable Uno package; no external script, CDN or global API patch.
(()=>{
'use strict';
if(globalThis.FastChartsSceneWebGL) return;
const contexts=new Map();
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
 const result=gl.createShader(type); if(!result) throw new Error('Could not allocate shader');
 gl.shaderSource(result,text); gl.compileShader(result);
 if(!gl.getShaderParameter(result,gl.COMPILE_STATUS)){const error=gl.getShaderInfoLog(result);gl.deleteShader(result);throw new Error(error);}
 return result;
}
function initialize(state){
 const gl=state.gl; const vertex=shader(gl,gl.VERTEX_SHADER,vertexSource); let fragment,program;
 try{
  fragment=shader(gl,gl.FRAGMENT_SHADER,fragmentSource); program=gl.createProgram();
  if(!program) throw new Error('Could not allocate shader program');
  gl.attachShader(program,vertex);gl.attachShader(program,fragment);gl.linkProgram(program);
  if(!gl.getProgramParameter(program,gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program));
  state.program=program;state.matrix=gl.getUniformLocation(program,'uMatrix');state.pointSize=gl.getUniformLocation(program,'uPointSize');
  state.vao=gl.createVertexArray();state.buffer=gl.createBuffer();gl.bindVertexArray(state.vao);gl.bindBuffer(gl.ARRAY_BUFFER,state.buffer);
  gl.enableVertexAttribArray(0);gl.vertexAttribPointer(0,3,gl.FLOAT,false,40,0);
  gl.enableVertexAttribArray(1);gl.vertexAttribPointer(1,4,gl.FLOAT,false,40,12);
  gl.enableVertexAttribArray(2);gl.vertexAttribPointer(2,3,gl.FLOAT,false,40,28);
  state.hasVertices=false;
 }catch(error){if(program)gl.deleteProgram(program);throw error;}
 finally{gl.deleteShader(vertex);if(fragment)gl.deleteShader(fragment);}
}
function create(id){
 if(contexts.has(id)) throw new Error('Duplicate chart context');
 const canvas=document.createElement('canvas');
 const gl=canvas.getContext('webgl2',{alpha:true,premultipliedAlpha:true,depth:true,stencil:false,antialias:false,preserveDrawingBuffer:false,powerPreference:'high-performance'});
 if(!gl) throw new Error('WebGL2 is unavailable; select the Skia fallback.');
 const state={canvas,gl,readback:null,flipped:null,hasVertices:false,lost:false};
 canvas.addEventListener('webglcontextlost',event=>{event.preventDefault();state.lost=true;});
 canvas.addEventListener('webglcontextrestored',()=>{state.lost=false;initialize(state);});
 try{initialize(state);contexts.set(id,state);}catch(error){gl.getExtension('WEBGL_lose_context')?.loseContext();throw error;}
 const debug=gl.getExtension('WEBGL_debug_renderer_info');
 return 'WebGL2 · '+(debug?gl.getParameter(debug.UNMASKED_RENDERER_WEBGL):gl.getParameter(gl.RENDERER));
}
function draw(id,vertices,metadata,width,height,pixels){
 const state=contexts.get(id);if(!state)throw new Error('Chart context is not initialized');
 const {gl,canvas}=state;if(state.lost||gl.isContextLost())throw new Error('The WebGL context was lost. Reload the chart to retry, or use Skia fallback.');
 if(!Number.isInteger(width)||!Number.isInteger(height)||width<1||height<1||width*height>16000000)throw new Error('Invalid chart framebuffer size');
 const frame=JSON.parse(metadata);
 if(canvas.width!==width||canvas.height!==height){canvas.width=width;canvas.height=height;state.readback=new Uint8Array(width*height*4);state.flipped=new Uint8Array(width*height*4);}
 if(!state.readback){state.readback=new Uint8Array(width*height*4);state.flipped=new Uint8Array(width*height*4);}
 gl.bindFramebuffer(gl.FRAMEBUFFER,null);gl.viewport(0,0,width,height);gl.disable(gl.SCISSOR_TEST);gl.disable(gl.CULL_FACE);
 gl.colorMask(true,true,true,true);gl.depthMask(true);gl.clearDepth(1);
 const hex=frame.background.replace('#','');const alpha=hex.length===8?parseInt(hex.slice(6,8),16)/255:1;
 gl.clearColor(parseInt(hex.slice(0,2),16)/255*alpha,parseInt(hex.slice(2,4),16)/255*alpha,parseInt(hex.slice(4,6),16)/255*alpha,alpha);
 gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);gl.useProgram(state.program);gl.bindVertexArray(state.vao);gl.bindBuffer(gl.ARRAY_BUFFER,state.buffer);
 // MemoryView is borrowed for this synchronous call only; slice owns the transferred upload.
 if(vertices.length>0){const bytes=vertices.slice();gl.bufferData(gl.ARRAY_BUFFER,bytes,gl.DYNAMIC_DRAW);state.hasVertices=true;}
 if(frame.vertexCount>0&&!state.hasVertices)throw new Error('GPU vertex data must be uploaded after context restoration');
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
 const state=contexts.get(id);if(!state)return;contexts.delete(id);const {gl}=state;
 if(!gl.isContextLost()){gl.deleteBuffer(state.buffer);gl.deleteVertexArray(state.vao);gl.deleteProgram(state.program);gl.getExtension('WEBGL_lose_context')?.loseContext();}
 state.readback=state.flipped=null;
}
globalThis.FastChartsSceneWebGL=Object.freeze({create,draw,destroy});
})();
