using System.Collections.ObjectModel;
using System.Windows;
using ClarionDbg.Engine;

namespace ClarionDbg.App;

public partial class BreakpointsWindow : Window
{
    readonly ObservableCollection<DebugSession.Breakpoint> _bps;
    readonly DebugSession? _session;

    public BreakpointsWindow(ObservableCollection<DebugSession.Breakpoint> bps, DebugSession? session)
    {
        InitializeComponent();
        _bps = bps;
        _session = session;
        Grid.ItemsSource = _bps;
    }

    // Condition / HitCondition / LogMessage edit the same object the engine holds, so they take
    // effect on the next hit with no extra wiring. Only enable/disable and remove need an action.
    void Enabled_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DebugSession.Breakpoint bp)
            _session?.SetBreakpointEnabled(bp, bp.Enabled);
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not DebugSession.Breakpoint bp) return;
        _session?.RemoveBreakpointLive(bp);
        _bps.Remove(bp);
    }

    void RemoveAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var bp in _bps.ToList()) _session?.RemoveBreakpointLive(bp);
        _bps.Clear();
    }

    void Close_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit();   // flush any in-progress cell edit back to the breakpoint object
        Close();
    }
}
