using System.ComponentModel;
using System.Runtime.InteropServices;
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
            bool launcher = string.Equals(process, InstalledLayout.LauncherPath(root), StringComparison.OrdinalIgnoreCase);
            if ((!launcher && !string.Equals(process, Path.Combine(root, InstalledLayout.StudioName), StringComparison.OrdinalIgnoreCase)) ||
                !InstalledEntry.AcceptsArguments(launcher, args)) return 2;
            using var child = InstalledEntry.Start(root, launcher, args);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or Win32Exception)
        {
            if (Environment.UserInteractive)
                MessageBoxW(0, "无法启动已安装版本。请关闭正在使用的 Studio 组件，再运行安装程序修复。", "Desktop Motion Studio", 0x10);
            return 4;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(nint owner, string text, string caption, uint type);
}
