using FastChartsScene.Core;
using FastChartsScene.Controls.Uno;
namespace PackageConsumer;
public static class AxisTicksConsumer
{
    public static AxisTicksEditor Create()
    {
        var axis = new Axis("x") { TickOptions = new(AxisTickMode.Custom, [new(0,"Start"), new(10,"Finish")], "F2") };
        return new AxisTicksEditor { Axis = axis };
    }
}
