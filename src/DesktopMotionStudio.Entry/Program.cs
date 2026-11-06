using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using DesktopMotionStudio.Core.Distribution;

namespace DesktopMotionStudio.Entry;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ||
            RuntimeInformation.OSArchitecture != Architecture.X64) return 3;

        try
        {
            string root = InstalledLayout.CurrentUserRoot();
            string? process = Environment.ProcessPath;
            if (args is ["--uninstall-worker", var owner, var parent])
                return UninstallWorker.Run(root, owner, parent);
            bool launcher = string.Equals(process, InstalledLayout.LauncherPath(root), StringComparison.OrdinalIgnoreCase);
            if (!launcher && args is ["--uninstall"] &&
                string.Equals(process, Path.Combine(root, InstalledLayout.StudioName), StringComparison.OrdinalIgnoreCase))
            {
                if (!Environment.UserInteractive ||
                    MessageBoxW(0, "卸载 Desktop Motion Studio？\n\n用户配置、备份和诊断会保留。仅清理可验证的程序文件和受管入口。",
                        "Desktop Motion Studio", 0x124) != 6) return 1;
                UninstallWorker.Start(root);
                return 0;
            }
            if ((!launcher && !string.Equals(process, Path.Combine(root, InstalledLayout.StudioName), StringComparison.OrdinalIgnoreCase)) ||
                !InstalledEntry.AcceptsArguments(launcher, args)) return 2;
            using var child = InstalledEntry.Start(root, launcher, args);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or Win32Exception or JsonException)
        {
            if (Environment.UserInteractive)
                MessageBoxW(0, "无法启动已安装版本。请关闭正在使用的 Studio 组件，再运行安装程序修复。", "Desktop Motion Studio", 0x10);
            return 4;
        }
    }

    internal static int ReportRemoval(UninstallResult result)
    {
        if (result.Busy)
        {
            MessageBoxW(0, "另一个安装或卸载操作正在进行。请稍后重试。", "Desktop Motion Studio", 0x30);
            return 4;
        }
        int preserved = result.PreservedProgramItems + result.Shortcuts.Preserved;
        string message = preserved > 0 || result.Shortcuts.Incomplete
            ? $"卸载注册已移除。保留了 {preserved} 项无法确认的文件或入口。部分入口可能需要手工处理。用户配置已保留。"
            : "卸载完成。用户配置、备份和本地诊断已保留。";
        MessageBoxW(0, message, "Desktop Motion Studio", 0x40);
        return 0;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(nint owner, string text, string caption, uint type);
}
