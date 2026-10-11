using FastChartsScene.Controls.Uno;
using Microsoft.UI.Xaml.Controls;
namespace PackageConsumer;
public static class PropertyGridConsumer
{
    public static PropertyGridControl Create()
    {
        var grid = new PropertyGridControl();
        using (grid.BeginUpdate())
        {
            grid.Add("Example", "Title", new TextBox { Text = "Independent host" });
            grid.Add("Example", "Visible", new CheckBox { IsChecked = true });
        }
        grid.Filter = "title";
        return grid;
    }
}
