using FastChartsScene.Charts;
using FastChartsScene.Controls.Uno;
using Microsoft.UI.Xaml;

namespace FastChartsScene.Explorer;

public sealed partial class ExplorerPage
{
    private readonly CursorResultsTable _cursorTable = new() { Visibility = Visibility.Collapsed, ResizeEnabled = true };
    private bool _cursorTableVisible;
    private double _cursorTableHeight = 210;
    private ChartDocument? _cursorTableOwner;
    internal CursorResultsTable CursorTable => _cursorTable;
    private void InitializeCursorTable()
    {
        _center.RowDefinitions.Add(new() { Height = new GridLength(0) });
        Microsoft.UI.Xaml.Controls.Grid.SetRow(_cursorTable, 2); _center.Children.Add(_cursorTable);
        _center.SizeChanged += (_, _) => ArrangeCursorTable();
        _center.RowDefinitions[1].RegisterPropertyChangedCallback(Microsoft.UI.Xaml.Controls.RowDefinition.HeightProperty,
            (_, _) => ArrangeCursorTable());
        _cursorTable.ResizeRequested += (_, request) =>
        {
            if (!_cursorTableVisible) return;
            _cursorTableHeight = request.Reset ? 210 : Math.Clamp(_cursorTable.ActualHeight + request.HeightChange, 0, 520);
            ArrangeCursorTable();
        };
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
    private void ArrangeCursorTable()
    {
        if (_center.RowDefinitions.Count < 3) return;
        var available = Math.Max(0, _center.ActualHeight - (_detailsVisible ? 168 : 0));
        // Keep at least 240 DIP for the chart when possible, and at least 55% of
        // the scarce space on short windows. Retain the preferred pane height so
        // temporary viewport changes do not overwrite the user's chosen size.
        var maximum = Math.Min(520, available - Math.Min(240, available * .55));
        var height = _cursorTableVisible ? Math.Clamp(_cursorTableHeight, Math.Min(140, maximum), maximum) : 0;
        if (_center.RowDefinitions[2].Height.Value != height) _center.RowDefinitions[2].Height = new GridLength(height);
    }
    private void ToggleCursorTable()
    {
        _cursorTableVisible = !_cursorTableVisible;
        _cursorTable.Visibility = _cursorTableVisible ? Visibility.Visible : Visibility.Collapsed;
        ArrangeCursorTable();
        Chart.CursorValuesEnabled = _cursorTableVisible;
        if (!_cursorTableVisible) _cursorTable.Clear();
    }
}
