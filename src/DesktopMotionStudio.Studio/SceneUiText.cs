using DesktopMotionStudio.Core.Bindings;
using DesktopMotionStudio.Core.Preview;
using DesktopMotionStudio.Core.Scenes;

namespace DesktopMotionStudio.Studio;

internal static class SceneUiText
{
    internal static string Availability(SceneLibraryItem item) => item.IsPlayable ? "可预览 · 可绑定到多个应用" : item.PlaybackError switch
    {
        SceneDataError.UnsupportedSchema or SceneDataError.UnsupportedRuntime => "作品暂不可用：需要受支持的播放版本。",
        _ => "作品暂不可用：文件缺失、受损或无法读取。"
    };

    internal static string Binding(BindingMutationResult result) => result.Status switch
    {
        BindingMutationStatus.Applied => "动效绑定已保存。",
        BindingMutationStatus.Unchanged => "此应用已使用所选作品。",
        BindingMutationStatus.ApplicationMissing => "此应用配置已被移除，请重新选择。",
        BindingMutationStatus.TargetUnavailable => "启动入口不可用，请返回我的应用确认入口。",
        BindingMutationStatus.SceneUnavailable => "作品暂不可用，原有绑定已保留。请刷新作品库。",
        BindingMutationStatus.StorageError => UiText.Storage(result.RepositoryError),
        _ => "绑定未完成，请刷新并重新选择应用和作品。"
    };

    internal static string Preview(PreviewSnapshot state) => state.Error == PreviewError.CleanupFailed
        ? "无法确认预览已清理。请关闭工作室后重试。"
        : state.Phase switch
        {
            PreviewPhase.Starting => "正在准备预览…",
            PreviewPhase.Playing => "正在预览 · 模拟交接，不会启动应用。",
            PreviewPhase.Stopping => "正在停止预览…",
            PreviewPhase.Completed => "预览已完成。",
            PreviewPhase.Skipped => "预览已跳过。",
            PreviewPhase.Stopped => "预览已停止。",
            PreviewPhase.Failed => "预览未完成，请刷新作品后重试。",
            _ => "预览不会启动任何应用。"
        };
}
