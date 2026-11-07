using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using DesktopMotionStudio.Core.Distribution;
using Microsoft.Win32;

namespace DesktopMotionStudio.Setup;

internal static class Program
{
    private const string Caption = "Desktop Motion Studio";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 0) return 2;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || RuntimeInformation.OSArchitecture != Architecture.X64)
            return Report("此安装包需要 Windows 11 x64。", 3);
        if (!Environment.UserInteractive) return 3;
        if (MessageBoxW(0, "为当前 Windows 用户安装或升级 Desktop Motion Studio？\n\n安装会保留已有的应用配置和绑定。",
                Caption, 0x124) != 6)
            return 1;

        try
        {
            using Stream manifest = Package("payload.manifest.json");
            using Stream archive = Package("payload.zip");
            using Stream entry = Package("entry.exe");
            string programs = Path.GetDirectoryName(InstalledLayout.CurrentUserRoot())!;
            string menu = Environment.GetFolderPath(Environment.SpecialFolder.Programs, Environment.SpecialFolderOption.DoNotVerify);
            using RegistryKey registration = Registry.CurrentUser.CreateSubKey(InstalledLayout.UninstallRegistryPath);
            var coordinator = new InstallCoordinator(programs, menu, new InstallRegistration(registration));
            return coordinator.Install(manifest, archive, entry) switch
            {
                InstallOutcome.Installed => Report("安装完成。可以从开始菜单打开 Desktop Motion Studio。", 0),
                InstallOutcome.Upgraded => Report("升级完成。原有桌面入口和用户配置可以继续使用。", 0),
                InstallOutcome.AlreadyInstalled => Report("此版本已安装。可以从开始菜单打开 Desktop Motion Studio。", 0),
                InstallOutcome.Busy => Report("另一个安装操作正在进行。请等待其完成后重试。", 4),
                InstallOutcome.RemovalPending => ResumeRemoval(),
                _ => Report("已存在不同版本的安装。请使用对应的安装包重试。", 5)
            };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 32 or 33)
        {
            return Report("安装文件正在使用。请关闭 Studio 并等待 Launcher 和 MotionHost 退出后重试。", 4);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidDataException or JsonException)
        {
            return Report("无法完成安装。请检查安装位置，关闭正在运行的 Studio 组件后重试。若升级中断，请重新运行同一版本的安装包。", 5);
        }
    }

    private static Stream Package(string name) => Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ??
        throw new InvalidDataException("The setup package is incomplete.");

    private static int ResumeRemoval()
    {
        Guid? owner = UninstallCoordinator.PendingOwner();
        if (owner is null) return Report("待恢复的卸载已发生变化。请重新运行安装程序。", 4);
        if (MessageBoxW(0, "检测到一次未完成的卸载。完成清理后，再运行此安装包重新安装。现在继续卸载？",
                Caption, 0x124) != 6) return 1;
        UninstallResult result = UninstallCoordinator.RunForCurrentUser(owner.Value, resumeOnly: true);
        return result.Busy ? Report("另一个安装操作正在进行。请稍后重试。", 4) :
            Report("卸载已结束。用户配置已保留。冲突或无法验证的文件需手工处理。", 0);
    }

    private static int Report(string message, int code)
    {
        if (Environment.UserInteractive) MessageBoxW(0, message, Caption, code == 0 ? 0x40u : 0x10u);
        return code;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(nint owner, string text, string caption, uint type);
}
