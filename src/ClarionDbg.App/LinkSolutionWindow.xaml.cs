using System.IO;
using System.Windows;
using Clarion.SourceResolution;
using Microsoft.Win32;

namespace ClarionDbg.App;

/// <summary>
/// First-open handshake: links a .sln to a chosen Clarion version (and, optionally,
/// an explicit ClarionProperties.xml for non-default ConfigDir installs / a forced
/// configuration). On Save it produces a <see cref="SolutionAssociation"/> the caller
/// persists, plus the version + properties path needed to build the resolver.
/// </summary>
public partial class LinkSolutionWindow : Window
{
    const string AutoConfig = "Auto-detect";

    readonly string _exePath;

    public string SolutionPath { get; private set; } = "";
    public ClarionCompilerVersion? Version { get; private set; }
    public string PropertiesFile { get; private set; } = "";
    public SolutionAssociation? Association { get; private set; }

    /// <summary>One pickable (install, version) pair; ToString drives the combo display.</summary>
    sealed record VersionChoice(ClarionInstallation Install, ClarionCompilerVersion Version)
    {
        public override string ToString() => $"{Version.Name}   (IDE {Install.IdeVersion})";
    }

    public LinkSolutionWindow(string slnPath, string exePath)
    {
        InitializeComponent();
        _exePath = exePath;
        TxtSln.Text = slnPath;

        CmbConfig.ItemsSource = new[] { AutoConfig, "Debug", "Release" };
        CmbConfig.SelectedIndex = 0;

        var choices = ClarionInstallationDetector.DetectInstallations()
            .SelectMany(i => i.CompilerVersions.Select(v => new VersionChoice(i, v)))
            .ToList();
        CmbVersion.ItemsSource = choices;
        if (choices.Count > 0)
            CmbVersion.SelectedIndex = 0;            // most recent install first
        else
        {
            TxtNote.Text = "No Clarion installation detected. Skip to use the legacy source search.";
            BtnSave.IsEnabled = false;
        }
    }

    void BrowseSln_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Clarion solution (*.sln)|*.sln" };
        if (!string.IsNullOrEmpty(TxtSln.Text))
        {
            try { dlg.InitialDirectory = Path.GetDirectoryName(TxtSln.Text); } catch { }
        }
        if (dlg.ShowDialog() == true) TxtSln.Text = dlg.FileName;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var sln = TxtSln.Text.Trim();
        if (!File.Exists(sln)) { TxtNote.Text = "That solution file doesn't exist."; return; }
        if (CmbVersion.SelectedItem is not VersionChoice choice) { TxtNote.Text = "Pick a Clarion version."; return; }

        var cfg = CmbConfig.SelectedItem as string;
        var configOverride = string.Equals(cfg, AutoConfig) ? null : cfg;

        SolutionPath = sln;
        Version = choice.Version;
        PropertiesFile = choice.Install.PropertiesPath;
        Association = new SolutionAssociation
        {
            VersionName = choice.Version.Name,
            // PropertiesFile stays null unless overriding a non-default location;
            // the install's own PropertiesPath is used to build the resolver.
            PropertiesFile = null,
            ConfigurationOverride = configOverride,
            ExePath = _exePath,
        };
        DialogResult = true;
    }

    void Skip_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
