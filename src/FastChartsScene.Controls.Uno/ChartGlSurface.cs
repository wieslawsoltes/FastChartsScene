using FastChartsScene.Core;
using FastChartsScene.Rendering;
using Silk.NET.OpenGL;
using global::Uno.WinUI.Graphics3DGL;

namespace FastChartsScene.Controls.Uno;

internal sealed class ChartGlSurface : GLCanvasElement
{
    private uint _program,_vao,_vbo;
    private int _matrix,_pointSize;
    private RenderFrame? _uploaded;
    private bool _es;
    public RenderFrame? Frame { get; set; }
    public string Driver { get; private set; }="Not initialized";
    public string? Error { get; private set; }
    public long Submissions { get; private set; }
    public event Action? Rendered;
    public ChartGlSurface():base(null)
    {
        IsHitTestVisible=false;
        // A Canvas gives an unsized child zero desired size. Uno creates this element's FBO
        // during Loaded, before the parent's queued draw supplies device-pixel dimensions.
        // Explicit initial dimensions participate in the normal measure/arrange pass;
        // never invoke Measure from Loading, which is itself raised by Measure.
        Width=1; Height=1;
    }
    private uint Shader(GL gl,ShaderType type,string body)
    {
        var shader=gl.CreateShader(type);
        try
        {
            gl.ShaderSource(shader,(_es?"#version 300 es\nprecision highp float;\n":"#version 330 core\n")+body); gl.CompileShader(shader);
            gl.GetShader(shader,ShaderParameterName.CompileStatus,out var status);
            if(status==0) throw new InvalidOperationException(gl.GetShaderInfoLog(shader)); return shader;
        }
        catch { gl.DeleteShader(shader); throw; }
    }
    protected override unsafe void Init(GL gl)
    {
        var oldVao=gl.GetInteger(GetPName.VertexArrayBinding); var oldBuffer=gl.GetInteger(GetPName.ArrayBufferBinding);
        uint vertex=0,fragment=0;
        try
        {
            Driver=gl.GetStringS(StringName.Renderer)??"Unknown OpenGL renderer";
            var version=gl.GetStringS(StringName.Version)??"";
            _es=OperatingSystem.IsBrowser()||version.Contains("OpenGL ES",StringComparison.OrdinalIgnoreCase)||version.Contains("WebGL",StringComparison.OrdinalIgnoreCase);
            vertex=Shader(gl,ShaderType.VertexShader,"""
                layout(location=0) in vec3 aPosition;
                layout(location=1) in vec4 aColor;
                layout(location=2) in vec3 aNormal;
                uniform mat4 uMatrix;
                uniform float uPointSize;
                out vec4 vColor;
                void main() {
                    gl_Position=uMatrix*vec4(aPosition,1.0);
                    gl_PointSize=uPointSize;
                    float light=1.0;
                    if(dot(aNormal,aNormal)>0.001) light=0.35+0.65*abs(dot(normalize(aNormal),normalize(vec3(0.4,0.8,0.6))));
                    vColor=vec4(aColor.rgb*light,aColor.a);
                }
                """);
            fragment=Shader(gl,ShaderType.FragmentShader,"""
                in vec4 vColor;
                out vec4 fragmentColor;
                void main() { fragmentColor=vColor; }
                """);
            _program=gl.CreateProgram(); gl.AttachShader(_program,vertex); gl.AttachShader(_program,fragment); gl.LinkProgram(_program);
            gl.GetProgram(_program,GLEnum.LinkStatus,out var linked);
            if(linked==0) throw new InvalidOperationException(gl.GetProgramInfoLog(_program));
            _matrix=gl.GetUniformLocation(_program,"uMatrix"); _pointSize=gl.GetUniformLocation(_program,"uPointSize");
            _vao=gl.GenVertexArray(); _vbo=gl.GenBuffer(); gl.BindVertexArray(_vao); gl.BindBuffer(BufferTargetARB.ArrayBuffer,_vbo);
            gl.EnableVertexAttribArray(0); gl.VertexAttribPointer(0,3,VertexAttribPointerType.Float,false,40,(void*)0);
            gl.EnableVertexAttribArray(1); gl.VertexAttribPointer(1,4,VertexAttribPointerType.Float,false,40,(void*)12);
            gl.EnableVertexAttribArray(2); gl.VertexAttribPointer(2,3,VertexAttribPointerType.Float,false,40,(void*)28);
            Error=null; _uploaded=null;
        }
        catch(Exception e) { Error=e.Message; OnDestroy(gl); throw; }
        finally
        {
            if(vertex!=0) gl.DeleteShader(vertex); if(fragment!=0) gl.DeleteShader(fragment);
            gl.BindVertexArray((uint)oldVao); gl.BindBuffer(BufferTargetARB.ArrayBuffer,(uint)oldBuffer);
        }
    }
    protected override void OnDestroy(GL gl)
    {
        if(_vbo!=0) gl.DeleteBuffer(_vbo); if(_vao!=0) gl.DeleteVertexArray(_vao); if(_program!=0) gl.DeleteProgram(_program);
        _vbo=_vao=_program=0; _uploaded=null;
    }
    protected override unsafe void RenderOverride(GL gl)
    {
        if(Frame is not { } frame||_program==0) return;
        var oldProgram=gl.GetInteger(GetPName.CurrentProgram); var oldVao=gl.GetInteger(GetPName.VertexArrayBinding);
        var oldBuffer=gl.GetInteger(GetPName.ArrayBufferBinding); var oldDepth=gl.GetInteger(GetPName.DepthFunc);
        var oldDepthMask=gl.GetInteger(GetPName.DepthWritemask)!=0;
        var blend=gl.IsEnabled(EnableCap.Blend); var depth=gl.IsEnabled(EnableCap.DepthTest); var cull=gl.IsEnabled(EnableCap.CullFace); var scissor=gl.IsEnabled(EnableCap.ScissorTest);
        var srcRgb=gl.GetInteger(GetPName.BlendSrcRgb); var dstRgb=gl.GetInteger(GetPName.BlendDstRgb);
        var srcAlpha=gl.GetInteger(GetPName.BlendSrcAlpha); var dstAlpha=gl.GetInteger(GetPName.BlendDstAlpha);
        var equationRgb=gl.GetInteger(GetPName.BlendEquationRgb); var equationAlpha=gl.GetInteger(GetPName.BlendEquationAlpha);
        int* oldScissor=stackalloc int[4]; gl.GetInteger(GetPName.ScissorBox,oldScissor);
        float* oldClear=stackalloc float[4]; gl.GetFloat(GetPName.ColorClearValue,oldClear);
        int* colorMask=stackalloc int[4]; gl.GetInteger(GetPName.ColorWritemask,colorMask);
        try
        {
            gl.Disable(EnableCap.ScissorTest); gl.Disable(EnableCap.CullFace); gl.DepthMask(true); gl.ColorMask(true,true,true,true);
            var background=ColorRgba.Parse(frame.Metadata.Background); gl.ClearColor(background.R,background.G,background.B,background.A);
            gl.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);
            gl.UseProgram(_program); gl.BindVertexArray(_vao); gl.BindBuffer(BufferTargetARB.ArrayBuffer,_vbo);
            if(!ReferenceEquals(frame,_uploaded))
            {
                fixed(float* data=frame.Vertices) gl.BufferData(BufferTargetARB.ArrayBuffer,(nuint)(frame.Vertices.Length*sizeof(float)),data,BufferUsageARB.DynamicDraw);
                _uploaded=frame;
            }
            gl.Enable(EnableCap.Blend); gl.BlendEquationSeparate(BlendEquationModeEXT.FuncAdd,BlendEquationModeEXT.FuncAdd);
            gl.BlendFuncSeparate(BlendingFactor.SrcAlpha,BlendingFactor.OneMinusSrcAlpha,BlendingFactor.One,BlendingFactor.OneMinusSrcAlpha);
            gl.Enable(EnableCap.ScissorTest); gl.DepthFunc(DepthFunction.Lequal);
            var sx=RenderSize.Width/frame.Metadata.Width; var sy=RenderSize.Height/frame.Metadata.Height;
            foreach(var batch in frame.Metadata.Batches)
            {
                if(batch.DepthTest) gl.Enable(EnableCap.DepthTest); else gl.Disable(EnableCap.DepthTest);
                gl.DepthMask(batch.DepthTest);
                var clip=batch.Clip; var left=(int)Math.Floor(clip.X*sx); var bottom=(int)Math.Floor((frame.Metadata.Height-clip.Bottom)*sy);
                gl.Scissor(left,bottom,(uint)Math.Max(0,Math.Ceiling(clip.Right*sx)-left),(uint)Math.Max(0,Math.Ceiling((frame.Metadata.Height-clip.Y)*sy)-bottom));
                fixed(float* matrix=batch.Transform) gl.UniformMatrix4(_matrix,1,false,matrix);
                gl.Uniform1(_pointSize,(float)(batch.PointSize*sx));
                gl.DrawArrays(batch.Kind==PrimitiveKind.Points?PrimitiveType.Points:PrimitiveType.Triangles,batch.Start,(uint)batch.Count);
            }
            var error=gl.GetError(); if(error!=GLEnum.NoError) throw new InvalidOperationException($"OpenGL draw failed: {error}");
            Submissions++; Error=null; Rendered?.Invoke();
        }
        catch(Exception e) { Error=e.Message; throw; }
        finally
        {
            gl.UseProgram((uint)oldProgram); gl.BindVertexArray((uint)oldVao); gl.BindBuffer(BufferTargetARB.ArrayBuffer,(uint)oldBuffer);
            gl.DepthFunc((DepthFunction)oldDepth); gl.DepthMask(oldDepthMask); gl.ColorMask(colorMask[0]!=0,colorMask[1]!=0,colorMask[2]!=0,colorMask[3]!=0);
            gl.BlendFuncSeparate((BlendingFactor)srcRgb,(BlendingFactor)dstRgb,(BlendingFactor)srcAlpha,(BlendingFactor)dstAlpha);
            gl.BlendEquationSeparate((BlendEquationModeEXT)equationRgb,(BlendEquationModeEXT)equationAlpha);
            gl.Scissor(oldScissor[0],oldScissor[1],(uint)oldScissor[2],(uint)oldScissor[3]); gl.ClearColor(oldClear[0],oldClear[1],oldClear[2],oldClear[3]);
            Restore(gl,EnableCap.Blend,blend); Restore(gl,EnableCap.DepthTest,depth); Restore(gl,EnableCap.CullFace,cull); Restore(gl,EnableCap.ScissorTest,scissor);
        }
    }
    private static void Restore(GL gl,EnableCap capability,bool enabled) { if(enabled) gl.Enable(capability); else gl.Disable(capability); }
}
