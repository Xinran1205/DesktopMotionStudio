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
        ShortcutHealthText.Text = bindingId is null ? "绑定作品后可创建桌面入口。" : "正在检查桌面入口…";
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
        ShortcutActionText.Text = "正在处理桌面入口…";
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
                ShortcutActionText.Text = "桌面入口操作未确认，请刷新后重试。";
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
        ShortcutStatus.NotCreated => "未创建桌面入口。",
        ShortcutStatus.Healthy => "桌面入口有效。",
        ShortcutStatus.Missing => "登记的入口已缺失，可以修复。",
        ShortcutStatus.RepairRequired => "桌面入口指向旧的启动器，请修复。",
        ShortcutStatus.OutOfScope => "桌面位置已改变，可在当前桌面重建。",
        ShortcutStatus.Conflict => "桌面入口冲突。现有文件已保留。",
        ShortcutStatus.Busy => "桌面入口正被使用，请稍后重试。",
        ShortcutStatus.StorageError => "无法读取或保存所有权记录，请检查配置。",
        ShortcutStatus.Ineligible => "请先启用可用的动效绑定并确认应用入口。",
        ShortcutStatus.Created => "桌面入口已保存并验证。",
        ShortcutStatus.Removed => "已清理桌面入口记录。",
        _ => "当前桌面或启动器不可用。"
    };
}
