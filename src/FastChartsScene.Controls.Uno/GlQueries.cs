using Silk.NET.OpenGL;
namespace FastChartsScene.Controls.Uno;
internal static class GlQueries
{
    // Silk exposes its scalar query helper for GLEnum, but its pointer overload for GetPName.
    public static int GetInteger(this GL gl,GetPName name)=>gl.GetInteger((GLEnum)name);
}
