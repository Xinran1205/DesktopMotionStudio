using System.Windows;
using DesktopMotionStudio.Core.Bindings;
using DesktopMotionStudio.Core.Shortcuts;

namespace DesktopMotionStudio.Studio;

public partial class MainWindow
{
    private readonly ManagedShortcutService? _shortcuts;
    private ShortcutStatus? _shortcutStatus;
    private int _shortcutReadVersion;

    private void RefreshShortcutDetails(string? bindingId, int version)
    {
        _shortcutStatus = null;
        ShortcutActionText.Text = string.Empty;
        ShortcutHealthText.Text = bindingId is null ? ProductText.Get("BindAWorkToCreateADesktopShortcut") : ProductText.Get("CheckingDesktopShortcut");
        if (bindingId is not null && _shortcuts is not null)
            _ = ReadShortcutAsync(bindingId, version);
    }

    private async Task ReadShortcutAsync(string bindingId, int version)
    {
        int readVersion = ++_shortcutReadVersion;
        ShortcutResult result;
        try { result = await Task.Run(() => _shortcuts!.Inspect(bindingId)); }
        catch (Exception) { result = new(ShortcutStatus.Unavailable); }
        if (_closed || _closing || version != _detailsVersion || readVersion != _shortcutReadVersion) return;
        _shortcutStatus = result.Status;
        ShortcutHealthText.Text = ShortcutText(result.Status);
        UpdateEnabled();
    }

    private void UpdateShortcutButtons()
    {
        bool canAct = !_busy && !_closing && _readable && _shortcuts is not null && Selected?.Binding.Binding is not null;
        bool playable = Selected?.Binding.Status == BindingResolutionStatus.Ready && Selected.Work?.IsPlayable == true;
        ShortcutCreateButton.IsEnabled = canAct && playable && _shortcutStatus == ShortcutStatus.NotCreated;
        ShortcutRepairButton.IsEnabled = canAct && playable && _shortcutStatus is
            ShortcutStatus.Missing or ShortcutStatus.RepairRequired or ShortcutStatus.OutOfScope;
        ShortcutRemoveButton.IsEnabled = canAct && _shortcutStatus is
            ShortcutStatus.Healthy or ShortcutStatus.Missing or ShortcutStatus.RepairRequired;
    }

    private async Task RunShortcutActionAsync(Func<ManagedShortcutService, string, ShortcutResult> action)
    {
        if (_busy || !_readable || _closing || _shortcuts is null ||
            Selected?.Binding.Binding?.BindingId is not string bindingId) return;

        int version = _detailsVersion;
        _busy = true;
        ShortcutActionText.Text = ProductText.Get("UpdatingDesktopShortcut");
        UpdateEnabled();
        try
        {
            ShortcutResult result = await Task.Run(() => action(_shortcuts, bindingId));
            if (!_closed && !_closing && string.Equals(Selected?.Binding.Binding?.BindingId, bindingId, StringComparison.OrdinalIgnoreCase))
            {
                if (version == _detailsVersion) ShortcutActionText.Text = ShortcutText(result.Status);
                await ReadShortcutAsync(bindingId, _detailsVersion);
            }
        }
        catch (Exception)
        {
            if (!_closed && !_closing && version == _detailsVersion)
                ShortcutActionText.Text = ProductText.Get("CouldNotConfirmTheShortcutOperationRefreshAnd");
        }
        finally
        {
            _busy = false;
            if (!_closed) UpdateEnabled();
        }
    }

    private async void OnCreateShortcut(object sender, RoutedEventArgs e)
    {
        if (ShortcutCreateButton.IsEnabled) await RunShortcutActionAsync((service, id) => service.Create(id));
    }

    private async void OnRepairShortcut(object sender, RoutedEventArgs e)
    {
        if (ShortcutRepairButton.IsEnabled) await RunShortcutActionAsync((service, id) => service.Repair(id));
    }

    private async void OnRemoveShortcut(object sender, RoutedEventArgs e)
    {
        if (ShortcutRemoveButton.IsEnabled) await RunShortcutActionAsync((service, id) => service.Remove(id));
    }

    internal static string ShortcutText(ShortcutStatus status) => status switch
    {
        ShortcutStatus.NotCreated => ProductText.Get("NoDesktopShortcutYet"),
        ShortcutStatus.Healthy => ProductText.Get("TheDesktopShortcutIsValid"),
        ShortcutStatus.Missing => ProductText.Get("TheRegisteredShortcutIsMissingAndCanBe"),
        ShortcutStatus.RepairRequired => ProductText.Get("TheShortcutPointsToAnOldLauncherRepair"),
        ShortcutStatus.OutOfScope => ProductText.Get("TheDesktopLocationChangedYouCanRecreateThe"),
        ShortcutStatus.Conflict => ProductText.Get("DesktopShortcutConflictExistingFilesHaveBeenKept"),
        ShortcutStatus.Busy => ProductText.Get("TheDesktopShortcutIsInUseTryAgain"),
        ShortcutStatus.StorageError => ProductText.Get("CouldNotReadOrSaveTheShortcutOwnership"),
        ShortcutStatus.Ineligible => ProductText.Get("EnableAnAvailableWorkAndConfirmTheApp"),
        ShortcutStatus.Created => ProductText.Get("DesktopShortcutSavedAndVerified"),
        ShortcutStatus.Removed => ProductText.Get("DesktopShortcutRecordRemoved"),
        _ => ProductText.Get("TheDesktopOrLauncherIsUnavailable")
    };
}
