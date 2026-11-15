using System.IO;
using System.Windows;
using System.Windows.Controls;
using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.State;
using Microsoft.Win32;

namespace DesktopMotionStudio.Studio;

public partial class ApplicationEditorWindow : Window
{
    private readonly ApplicationRegistry _registry;
    private readonly ApplicationProfile? _profile;
    private ApplicationCandidate[] _candidates = [];
    private CancellationTokenSource? _discoveryCancellation;
    private int _scanVersion;
    private bool _closed;
    private bool _saving;
    internal ApplicationRegistryResult? Result { get; private set; }

    internal ApplicationEditorWindow(ApplicationRegistry registry, ApplicationProfile? profile = null)
    {
        InitializeComponent();
        _registry = registry;
        _profile = profile;
        Width = Math.Min(Width, Math.Max(MinWidth, SystemParameters.WorkArea.Width - 80));
        Height = Math.Min(Height, Math.Max(MinHeight, SystemParameters.WorkArea.Height - 80));
        if (profile is not null)
        {
            Title = Heading.Text = ProductText.Get("ConfirmLaunchEntry");
            DisplayNameBox.Text = profile.DisplayName;
            DisplayNameBox.IsReadOnly = true;
            Intro.Text = profile.Target is null ? ProductText.Get("ChooseAndConfirmAnEntryTypeTheOriginal") : ProductText.Get("ChangeThisAppSLaunchEntryAndKeep");
            if (profile.Target is { Kind: not ApplicationTargetKind.Packaged } target)
            {
                KindBox.SelectedIndex = target.Kind == ApplicationTargetKind.Shortcut ? 1 : 0;
                PathBox.Text = target.Identifier;
                ArgumentsBox.Text = string.Join(Environment.NewLine, target.Arguments);
                WorkingDirectoryBox.Text = target.WorkingDirectory ?? string.Empty;
            }
            else if (profile.Target?.Kind == ApplicationTargetKind.Packaged) SourceTabs.SelectedIndex = 1;
        }
        Loaded += async (_, _) => { DisplayNameBox.Focus(); await DiscoverAsync(); };
        Closing += (_, e) => { if (_saving) e.Cancel = true; };
        Closed += (_, _) => { _closed = true; _scanVersion++; _discoveryCancellation?.Cancel(); };
    }

    private async Task DiscoverAsync()
    {
        _discoveryCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _discoveryCancellation = cancellation;
        int version = ++_scanVersion;
        DiscoveryStatusText.Text = ProductText.Get("LookingForLocalApps");
        try
        {
            ApplicationDiscoveryResult result = await new DiscoveryClient().ScanAsync(cancellation.Token);
            if (_closed || version != _scanVersion) return;
            _candidates = result.Candidates.ToArray();
            FilterDiscovery();
            DiscoveryStatusText.Text = result.Status switch
            {
                ApplicationDiscoveryStatus.Complete => ProductText.Format("EntriesFoundCheckTheNameAndIdentityBefore", _candidates.Length),
                ApplicationDiscoveryStatus.Empty => ProductText.Get("NoEntriesFoundYouCanChooseOneUnder"),
                ApplicationDiscoveryStatus.TimedOut => ProductText.Get("TheScanTimedOutRetryOrSelectA"),
                ApplicationDiscoveryStatus.Cancelled => ProductText.Get("ScanCancelled"),
                _ => ProductText.Get("TheScanDidNotFinishRetryOrSelect")
            };
        }
        finally { cancellation.Dispose(); if (ReferenceEquals(_discoveryCancellation, cancellation)) _discoveryCancellation = null; }
    }

    private void FilterDiscovery()
    {
        string query = DiscoverySearchBox.Text.Trim();
        ApplicationCandidate? previous = DiscoveryList.SelectedItem as ApplicationCandidate;
        ApplicationCandidate[] visible = _candidates.Where(item => item.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        DiscoveryList.ItemsSource = visible;
        DiscoveryList.SelectedItem = visible.FirstOrDefault(item => item == previous);
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = ProductText.Get("ProgramsAndShortcutsExeLnkProgramsExeShortcuts"), CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        PathBox.Text = dialog.FileName;
        KindBox.SelectedIndex = Path.GetExtension(dialog.FileName).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (string.IsNullOrWhiteSpace(DisplayNameBox.Text)) DisplayNameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_saving) return;
        ApplicationTargetDefinition? target;
        if (SourceTabs.SelectedIndex == 1)
            target = (DiscoveryList.SelectedItem as ApplicationCandidate)?.Target;
        else
        {
            bool executable = KindBox.SelectedIndex == 0;
            string[] arguments = executable && ArgumentsBox.Text.Length > 0 ? ArgumentsBox.Text.Replace("\r\n", "\n").Split('\n') : [];
            target = new(executable ? ApplicationTargetKind.Executable : ApplicationTargetKind.Shortcut,
                PathBox.Text, arguments, executable && WorkingDirectoryBox.Text.Length > 0 ? WorkingDirectoryBox.Text : null);
        }
        if (target is null) { StatusText.Text = ProductText.Get("ChooseALaunchEntryFirst"); return; }
        string name = DisplayNameBox.Text;
        _saving = true;
        SaveButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        try
        {
            ApplicationRegistryResult result = await Task.Run(() => _profile is null ? _registry.Add(name, target) : _registry.Repair(_profile.AppId, target));
            if (_closed) return;
            Result = result;
            StatusText.Text = UiText.Result(result);
            if (result.Succeeded || (_profile is null && result.Status == ApplicationRegistryStatus.Duplicate))
            {
                _saving = false;
                DialogResult = true;
            }
        }
        catch (Exception) { if (!_closed) StatusText.Text = ProductText.Get("TheSaveDidNotCompleteCheckYourConfiguration"); }
        finally
        {
            _saving = false;
            if (!_closed) { SaveButton.IsEnabled = true; CancelButton.IsEnabled = true; }
        }
    }

    private void OnKindChanged(object sender, SelectionChangedEventArgs e) { if (Advanced is not null) Advanced.IsEnabled = KindBox.SelectedIndex == 0; }
    private void OnDiscoverySearch(object sender, TextChangedEventArgs e) { if (DiscoveryList is not null) FilterDiscovery(); }
    private void OnCandidateSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_profile is null && string.IsNullOrWhiteSpace(DisplayNameBox.Text) && DiscoveryList.SelectedItem is ApplicationCandidate candidate)
            DisplayNameBox.Text = candidate.DisplayName;
    }
    private async void OnDiscover(object sender, RoutedEventArgs e) => await DiscoverAsync();
}
