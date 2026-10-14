using FastChartsScene.Interaction;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FastChartsScene.Controls.Uno;

public sealed partial class CursorResultsTable
{
    public static readonly DependencyProperty NumberFormatProperty = DependencyProperty.Register(
        nameof(NumberFormat), typeof(CursorValueFormat), typeof(CursorResultsTable),
        new PropertyMetadata(null, (d, _) => ((CursorResultsTable)d).RefreshNumberFormat()));
    public static readonly DependencyProperty NumberFormatSelectionEnabledProperty = DependencyProperty.Register(
        nameof(NumberFormatSelectionEnabled), typeof(bool), typeof(CursorResultsTable),
        new PropertyMetadata(false, (d, _) => ((CursorResultsTable)d).UpdateFormatAvailability()));
    /// <summary>Display-only formats. Null dependency-property values mean the default
    /// G9/G9. Changing format preserves Hold, row identity, exact details and CSV.</summary>
    public CursorValueFormat NumberFormat
    {
        get => (CursorValueFormat?)GetValue(NumberFormatProperty) ?? CursorValueFormat.Default;
        set => SetValue(NumberFormatProperty, value ?? CursorValueFormat.Default);
    }
    /// <summary>Opt into the native format editor and its compact toolbar button.</summary>
    public bool NumberFormatSelectionEnabled
    {
        get => (bool)GetValue(NumberFormatSelectionEnabledProperty);
        set => SetValue(NumberFormatSelectionEnabledProperty, value);
    }
    private bool _formatReady;
    private void UpdateFormatAvailability()
    {
        if (_formatReady) NumberFormatButton.Visibility = NumberFormatSelectionEnabled ? Visibility.Visible : Visibility.Collapsed;
    }
    private void RefreshNumberFormat()
    {
        if (!_formatReady) return;
        var format = NumberFormat;
        foreach (var row in _rows) if (row.SetFormat(format)) UpdatedRows++;
    }
    private void FormatEditorOpened(object sender, object e)
    {
        var format = NumberFormat;
        XFormatDraft.Text = format.XFormat; ValueFormatDraft.Text = format.ValueFormat; FormatError.Text = "";
    }
    private void ApplyNumberFormat(object sender, RoutedEventArgs e)
    {
        try
        {
            // Construct BOTH formats before mutating the table. Invalid drafts stay
            // open for editing and never partially apply or release a held snapshot.
            var candidate = new CursorValueFormat(XFormatDraft.Text, ValueFormatDraft.Text);
            NumberFormat = candidate; FormatError.Text = ""; NumberFormatFlyout.Hide();
        }
        catch (ArgumentException error) { FormatError.Text = error.Message; }
    }
    private void ApplyFormatPreset(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string format }) return;
        NumberFormat = new CursorValueFormat(format, format); FormatError.Text = ""; NumberFormatFlyout.Hide();
    }
    private void CancelNumberFormat(object sender, RoutedEventArgs e) => NumberFormatFlyout.Hide();
}
