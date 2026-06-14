using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ClarionDbg.Engine;
using Microsoft.Win32;

namespace ClarionDbg.App;

public partial class MainWindow : Window
{
    enum State { Idle, Running, Stopped }

    PeImage? _pe;
    TswdInfo? _info;
    DebugSession? _session;
    State _state = State.Idle;
    string? _exePath, _srcPath;

    readonly ObservableCollection<SourceLine> _lines = new();
    readonly ObservableCollection<VarRow> _vars = new();
    readonly ObservableCollection<VarRow> _localsRows = new();

    public MainWindow()
    {
        InitializeComponent();
        SourceList.ItemsSource = _lines;
        GridVars.ItemsSource = _vars;
        GridLocals.ItemsSource = _localsRows;
        Loaded += (_, _) =>
        {
            // auto-load the sample for an immediate demo
            var sample = @"C:\ai\debuger\sample\dbgtest\dbgtest_dbg.exe";
            if (File.Exists(sample)) LoadExe(sample);
        };
    }

    // ---------- loading ----------
    void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Clarion debug EXE (*.exe)|*.exe" };
        if (dlg.ShowDialog() == true) LoadExe(dlg.FileName);
    }

    void LoadExe(string path)
    {
        try
        {
            _exePath = path;
            _pe = new PeImage(path);
            _info = TswdInfo.Load(_pe);
            if (_info == null) { Log("No .cwdebug info — this EXE was not built in Debug mode (vid=full)."); return; }

            _srcPath = ResolveSource(path, _info.SourceFile);
            TxtSourceName.Text = _srcPath ?? _info.SourceFile;
            LoadSource();
            LstProcs.ItemsSource = _info.Procedures.OrderBy(p => p.Rva)
                                        .Select(p => $"{p.Name}  @0x{p.Rva:X}").ToList();

            Log($"Loaded {Path.GetFileName(path)} — {_info.Lines.Count} line entries, " +
                $"{_info.Globals.Count} globals, {_info.Procedures.Count} procedures.");
            // pre-set a demonstration breakpoint where all globals are populated
            SetBreakpointAtLine(21);
            Status($"Loaded {Path.GetFileName(path)}. Press Go to run.");
        }
        catch (Exception ex) { Log("Load error: " + ex.Message); }
    }

    string? ResolveSource(string exePath, string srcName)
    {
        foreach (var dir in new[] { Path.GetDirectoryName(exePath)!,
                                    @"C:\ai\debuger\sample\dbgtest" })
        {
            var p = Path.Combine(dir, srcName);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    void LoadSource()
    {
        _lines.Clear();
        if (_srcPath == null) { Log("Source file not found next to EXE."); return; }
        int n = 1;
        foreach (var line in File.ReadAllLines(_srcPath))
            _lines.Add(new SourceLine { LineNo = n++, Text = line.Replace("\t", "    ") });
    }

    // ---------- breakpoints ----------
    void Gutter_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is SourceLine sl)
        {
            sl.HasBreakpoint = !sl.HasBreakpoint;
            Log(sl.HasBreakpoint ? $"Breakpoint set at line {sl.LineNo}." : $"Breakpoint cleared at line {sl.LineNo}.");
        }
    }

    void SetBreakpointAtLine(int line)
    {
        var sl = _lines.FirstOrDefault(l => l.LineNo == line);
        if (sl != null) sl.HasBreakpoint = true;
    }

    // ---------- run control ----------
    void BtnGo_Click(object sender, RoutedEventArgs e)
    {
        if (_state == State.Stopped) { ClearCurrentLine(); _session!.Continue(); SetState(State.Running); Status("Running…"); return; }
        if (_state == State.Running) return;
        if (_pe == null || _info == null || _exePath == null) { Log("Open a debug EXE first."); return; }

        var bpLines = _lines.Where(l => l.HasBreakpoint).Select(l => l.LineNo).ToList();
        if (bpLines.Count == 0) { Log("Set at least one breakpoint (click the gutter)."); return; }

        _vars.Clear(); _localsRows.Clear(); LstStack.ItemsSource = null;
        _session = new DebugSession(_exePath, _pe, _info);
        _session.Log += s => Dispatcher.Invoke(() => Log(s));
        _session.Stopped += OnStopped;
        _session.Exited += code => Dispatcher.Invoke(() =>
        {
            Log($"--- debuggee exited (code {code}) ---");
            ClearCurrentLine(); SetState(State.Idle); Status("Exited.");
        });
        _session.Start(bpLines);
        SetState(State.Running);
        Status("Running…");
    }

    void OnStopped(DebugSession.StopInfo info) => Dispatcher.Invoke(() =>
    {
        SetState(State.Stopped);
        Log($"Stopped: {info.Reason} at EIP 0x{info.Eip:X8}" + (info.Line is int l ? $" (line {l})" : ""));

        ClearCurrentLine();
        if (info.Line is int line)
        {
            var sl = _lines.FirstOrDefault(x => x.LineNo == line);
            if (sl != null) { sl.IsCurrent = true; SourceList.UpdateLayout();
                ((FrameworkElement?)SourceList.ItemContainerGenerator.ContainerFromItem(sl))?.BringIntoView(); }
        }

        LstStack.ItemsSource = info.Stack.Select((f, i) =>
            $"#{i} {f.Proc}  0x{f.Addr:X8}" + (f.Line is int fl ? $"  line {fl}" : "")).ToList();

        _localsRows.Clear();
        foreach (var v in info.Locals)
            _localsRows.Add(new VarRow { Name = v.Name, Type = v.TypeName, Value = v.Display, Address = $"0x{v.Addr:X8}" });

        _vars.Clear();
        foreach (var v in info.Globals)
            _vars.Add(new VarRow { Name = v.Name, Type = v.TypeName, Value = v.Display, Address = $"0x{v.Addr:X8}" });

        Status($"Stopped at line {info.Line}. Press Go to continue.");
    });

    void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        _session?.Terminate();
        ClearCurrentLine(); SetState(State.Idle); Status("Stopped.");
    }

    void LstProcs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { }

    // ---------- helpers ----------
    void ClearCurrentLine() { foreach (var l in _lines) if (l.IsCurrent) l.IsCurrent = false; }

    void SetState(State s)
    {
        _state = s;
        BtnGo.IsEnabled = s != State.Running;
        BtnStop.IsEnabled = s != State.Idle;
        BtnGo.Content = s == State.Stopped ? "▶  Continue  (F5)" : "▶  Go  (F5)";
    }

    void Log(string s) { TxtLog.AppendText(s + "\n"); TxtLog.ScrollToEnd(); }
    void Status(string s) => TxtStatus.Text = s;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F5) { BtnGo_Click(this, new RoutedEventArgs()); e.Handled = true; }
        base.OnKeyDown(e);
    }
}

public sealed class SourceLine : INotifyPropertyChanged
{
    public int LineNo { get; set; }
    public string Text { get; set; } = "";

    bool _bp, _cur;
    public bool HasBreakpoint { get => _bp; set { _bp = value; Raise(nameof(BpVisibility)); } }
    public bool IsCurrent { get => _cur; set { _cur = value; Raise(nameof(RowBg)); } }

    public Visibility BpVisibility => _bp ? Visibility.Visible : Visibility.Collapsed;
    public Brush RowBg => _cur ? (Brush)System.Windows.Application.Current.Resources["CurLine"]
                               : Brushes.Transparent;

    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

public sealed class VarRow
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Value { get; set; } = "";
    public string Address { get; set; } = "";
}
