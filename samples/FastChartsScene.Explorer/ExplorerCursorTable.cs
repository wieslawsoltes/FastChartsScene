using FastChartsScene.Charts;
using FastChartsScene.Controls.Uno;
using Microsoft.UI.Xaml;

namespace FastChartsScene.Explorer;

public sealed partial class ExplorerPage
{
    private readonly CursorResultsTable _cursorTable = new() { Visibility = Visibility.Collapsed };
    private bool _cursorTableVisible;
    private ChartDocument? _cursorTableOwner;
    internal CursorResultsTable CursorTable => _cursorTable;
    private void InitializeCursorTable()
    {
        _center.RowDefinitions.Add(new() { Height = new GridLength(0) });
        Microsoft.UI.Xaml.Controls.Grid.SetRow(_cursorTable, 2); _center.Children.Add(_cursorTable);
        Chart.SeriesCursorValuesChanged += (_, _) =>
        {
            if (!ReferenceEquals(_cursorTableOwner, Chart.CursorValuesDocument))
            { _cursorTableOwner = Chart.CursorValuesDocument; _cursorTable.Clear(); }
            _cursorTable.SetValues(Chart.SeriesCursorValues, Chart.CursorValuesAreActive);
        };
        _cursorTable.ExportRequested += async (_, _) =>
        {
            try
            {
                await SaveFile(System.Text.Encoding.UTF8.GetBytes(_cursorTable.ExportCsv()), "cursor-values.csv", "text/csv", ".csv");
                LastCommandError = null;
            }
            catch (Exception e) { LastCommandError = e.Message; }
            UpdateStatus();
        };
    }
    private void ToggleCursorTable()
    {
        _cursorTableVisible = !_cursorTableVisible;
        _cursorTable.Visibility = _cursorTableVisible ? Visibility.Visible : Visibility.Collapsed;
        _center.RowDefinitions[2].Height = new GridLength(_cursorTableVisible ? 210 : 0);
        Chart.CursorValuesEnabled = _cursorTableVisible;
        if (!_cursorTableVisible) _cursorTable.Clear();
    }
}
