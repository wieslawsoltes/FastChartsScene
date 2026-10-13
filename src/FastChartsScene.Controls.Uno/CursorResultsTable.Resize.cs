using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace FastChartsScene.Controls.Uno;

/// <summary>A request to change the height of a bottom-docked results pane. The host
/// owns layout constraints. Positive HeightChange grows the pane; Reset requests the
/// host's default size. No chart or parent layout is mutated by the table itself.</summary>
public sealed class CursorTableResizeEventArgs(double heightChange, bool reset = false) : EventArgs
{
    public double HeightChange { get; } = heightChange;
    public bool Reset { get; } = reset;
}

public sealed partial class CursorResultsTable
{
    public static readonly DependencyProperty ResizeEnabledProperty = DependencyProperty.Register(
        nameof(ResizeEnabled), typeof(bool), typeof(CursorResultsTable), new PropertyMetadata(false,
            (d, _) => ((CursorResultsTable)d).UpdateResizeAvailability()));
    /// <summary>Opt-in native resize grip. Subscribe to ResizeRequested to apply the
    /// request within the host's constraints. Disabled by default for standalone hosts.</summary>
    public bool ResizeEnabled { get => (bool)GetValue(ResizeEnabledProperty); set => SetValue(ResizeEnabledProperty, value); }
    public event EventHandler<CursorTableResizeEventArgs>? ResizeRequested;
    private void UpdateResizeAvailability()
    {
        if (ResizeGrip == null) return;
        ResizeGrip.Visibility = ResizeEnabled ? Visibility.Visible : Visibility.Collapsed;
        ResizeRow.Height = new GridLength(ResizeEnabled ? 6 : 0);
    }
    private void ResizeDragged(object sender, DragDeltaEventArgs e)
    {
        if (ResizeEnabled && double.IsFinite(e.VerticalChange) && e.VerticalChange != 0)
            ResizeRequested?.Invoke(this, new(-e.VerticalChange));
    }
    private void ResizePressed(object sender, PointerRoutedEventArgs e) => ResizeGrip.Focus(FocusState.Pointer);
    private void ResizeDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (!ResizeEnabled) return;
        ResizeRequested?.Invoke(this, new(0, true)); e.Handled = true;
    }
    private void ResizeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!ResizeEnabled) return;
        var delta = e.Key switch { VirtualKey.Up => 12, VirtualKey.Down => -12, VirtualKey.PageUp => 48, VirtualKey.PageDown => -48, _ => 0 };
        if (delta == 0 && e.Key != VirtualKey.Home) return;
        ResizeRequested?.Invoke(this, new(delta, e.Key == VirtualKey.Home)); e.Handled = true;
    }
}
