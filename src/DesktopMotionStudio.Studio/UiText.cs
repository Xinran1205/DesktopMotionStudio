using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.State;

namespace DesktopMotionStudio.Studio;

internal static class UiText
{
    internal static string Storage(RepositoryError error) => error switch
    {
        RepositoryError.UnsupportedSchema => "配置来自较新版本，请使用相应版本打开。现有配置已保留。",
        RepositoryError.Busy => "配置正在被另一项操作使用，请稍后重试。",
        RepositoryError.InvalidJson or RepositoryError.InvalidState => "配置内容无法读取。请保留当前文件，并检查可用备份。",
        _ => "无法读写配置，请检查文件权限和可用空间后重试。"
    };

    internal static string Result(ApplicationRegistryResult result) => result.Status switch
    {
        ApplicationRegistryStatus.Applied => "已保存。",
        ApplicationRegistryStatus.Unchanged => "配置已是当前状态。",
        ApplicationRegistryStatus.Duplicate => $"此启动入口已添加为“{result.Profile?.DisplayName}”。",
        ApplicationRegistryStatus.TargetUnavailable => "启动入口不可用，请重新选择本地程序、快捷方式或已安装应用。",
        ApplicationRegistryStatus.ApplicationMissing => "此应用配置已被移除，请刷新列表。",
        ApplicationRegistryStatus.StorageError => Storage(result.RepositoryError),
        _ => "输入不完整或超出允许范围，请检查名称与启动入口。"
    };

    internal static string Kind(ApplicationTargetKind? kind) => kind switch
    {
        ApplicationTargetKind.Executable => "本地程序",
        ApplicationTargetKind.Shortcut => "快捷方式",
        ApplicationTargetKind.Packaged => "打包应用",
        _ => "待确认目标"
    };
}
