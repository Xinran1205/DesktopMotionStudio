using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.Localization;
using DesktopMotionStudio.Core.State;
using static StudioUiChecks;

internal static class F17LanguageChecks
{
    private const string CustomName = "零界";
    private const string HomeTitle = "Desktop Motion Studio";
    private const string PreviewTitle = "Desktop Motion Studio — Preview";

    internal static int Run()
    {
        string studio = Environment.GetEnvironmentVariable("DMS_F08_STUDIO_PATH") ?? "";
        string target = Environment.GetEnvironmentVariable("DMS_F06_TARGET_PATH") ?? "";
        if (!File.Exists(studio) || !File.Exists(target))
        {
            Console.WriteLine("FAIL F17 UI requires published Studio and an isolated WindowHost fixture.");
            return 1;
        }

        var root = Directory.CreateTempSubdirectory("dms-f17-");
        string profile = Path.Combine(root.FullName, "profile.json");
        string sidecar = profile + ".language";
        string bundle = Path.GetDirectoryName(studio)!;
        string catalog = Path.Combine(bundle, "Scenes");
        var store = new StudioStateRepository(profile);
        var preference = new StudioLanguagePreference(profile);
        Process? process = null;
        try
        {
            Require(preference.Read() == ProductText.DefaultLanguage, "Missing preference did not fall back.");
            File.WriteAllText(sidecar, new string('x', 100));
            Require(preference.Read() == ProductText.DefaultLanguage && !preference.TrySave("fr"),
                "Invalid preference or token was accepted.");
            Directory.CreateDirectory(sidecar + ".directory");
            string blockedProfile = sidecar + ".directory" + Path.DirectorySeparatorChar + "profile.json";
            Directory.CreateDirectory(blockedProfile + ".language");
            var blocked = new StudioLanguagePreference(blockedProfile);
            Require(!blocked.TrySave(ProductText.English) && blocked.Read() == ProductText.DefaultLanguage,
                "An unwritable preference did not report failure and fall back.");
            var app = new ApplicationProfile("custom-app", CustomName, target,
                new(ApplicationTargetKind.Executable, target, ["custom argument 中文"], Path.GetDirectoryName(target)));
            var state = StudioState.Empty with { Applications = [app], Bindings = [new("custom-binding", app.AppId, "zero-boundary", true)] };
            Require(store.Save(StudioState.Empty) == RepositoryError.None && store.Save(state) == RepositoryError.None,
                "Profile and backup fixture could not be saved.");
            byte[] original = File.ReadAllBytes(profile), backup = File.ReadAllBytes(profile + ".bak");
            int baseline = CountTargets(target);

            process = Start(studio, profile, catalog);
            nint main = Window(process, HomeTitle);
            var home = AutomationElement.FromHandle(main);
            Require(Program.WaitFor(() => ReadValue(home, "应用显示名称") == CustomName), "Chinese home did not retain the custom name.");
            Switch(process, home, english: true, save: false);
            Require(File.ReadAllText(sidecar) == new string('x', 100), "Cancel rewrote the existing preference.");
            Switch(process, home, english: true);
            Require(preference.Read() == ProductText.English, "English was not persisted.");
            Require(new StudioLanguagePreference(Path.Combine(root.FullName, "another.json")).Read() == ProductText.DefaultLanguage,
                "A separate profile inherited the fixture language.");
            Require(Program.WaitFor(() => Status(home) == "语言已保存。请重新打开 Studio 应用新语言。"),
                "The home did not explain when the new language applies.");
            Stop(process);

            process = Start(studio, profile, catalog);
            main = Window(process, HomeTitle);
            home = AutomationElement.FromHandle(main);
            Require(Program.WaitFor(() => ReadValue(home, "App display name") == CustomName), "English home altered the custom name.");
            Require(HasName(home, "Zero Boundary"), "Home did not localize the bound work.");
            Invoke(home, "Open motion library");
            nint libraryHandle = Window(process, "Motion library");
            var library = AutomationElement.FromHandle(libraryHandle);
            Require(Program.WaitFor(() => HasName(library, "Zero Boundary") && HasName(library, "Studio Pulse (Development work)")),
                "English library did not show both translated built-in names.");
            var list = Find(library, "Complete motion works");
            ((SelectionItemPattern)Find(list, "Zero Boundary").GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            Require(Program.WaitFor(() => HasName(library, "Technology · Precision frames · Graphite and ice blue")),
                "English library did not translate presentation details.");
            Invoke(library, "Preview work");
            nint stage = 0;
            string host = Path.Combine(bundle, "MotionHost", "DesktopMotionStudio.MotionHost.exe");
            Require(Program.WaitFor(() => (stage = FindOwnedMotionWindow(host, PreviewTitle)) != 0), "The actual English preview did not open.");
            Keys(stage, 0x09);
            var stop = Find(AutomationElement.FromHandle(stage), "Stop preview");
            Require(!stop.Current.IsOffscreen && stop.Current.HasKeyboardFocus, "English stop control did not receive keyboard focus.");
            Keys(stage, 0x0D);
            Require(Program.WaitFor(() => HasName(library, "Preview skipped.")), "English preview completion message was absent.");
            Invoke(library, "Return to my apps");
            Require(Program.WaitFor(() => FindWindow(process.Id, "Motion library") == 0), "Library did not return to the home.");
            Switch(process, home, english: false);
            Require(preference.Read() == ProductText.DefaultLanguage, "Chinese was not persisted.");
            Stop(process);

            process = Start(studio, profile, catalog);
            home = AutomationElement.FromHandle(Window(process, HomeTitle));
            Require(Program.WaitFor(() => ReadValue(home, "应用显示名称") == CustomName), "Chinese home did not return after reopening.");
            Require(CountTargets(target) == baseline && File.ReadAllBytes(profile).SequenceEqual(original) &&
                File.ReadAllBytes(profile + ".bak").SequenceEqual(backup) && store.Load().IsSuccess,
                "Language switching or preview altered the profile, backup, or target process count.");
            Console.WriteLine("PASS F17 Chinese → English → Chinese, built-in works, native preview, fallbacks and exact profile/backup preservation.");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine($"FAIL F17 language UI: {error}");
            return 1;
        }
        finally
        {
            if (process is not null) Stop(process);
            root.Delete(recursive: true);
        }
    }

    private static void Switch(Process process, AutomationElement home, bool english, bool save = true)
    {
        Invoke(home, english ? "打开设置" : "Open settings");
        string title = english ? "设置" : "Settings";
        var settings = AutomationElement.FromHandle(Window(process, title));
        var display = settings.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "DisplayBox"));
        Require(display?.Current.Name == (english ? "动效显示区域" : "Animation display area"), "Display selector label was incorrect.");
        var choices = settings.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "LanguageBox"))
            ?? throw new InvalidOperationException("Language selector was absent.");
        Require(choices.Current.Name == (english ? "界面语言" : "Interface language"), "Language selector label was incorrect.");
        Require(Program.WaitFor(() => choices.Current.IsEnabled), "Language selector did not load.");
        var dropDown = (ExpandCollapsePattern)choices.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        dropDown.Expand();
        var option = Find(choices, english ? "English" : "简体中文");
        ((SelectionItemPattern)option.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        dropDown.Collapse();
        string button = save ? (english ? "保存设置" : "Save settings") : (english ? "取消设置" : "Cancel settings");
        Invoke(settings, button);
        Require(Program.WaitFor(() => FindWindow(process.Id, title) == 0), "Language settings did not close.");
    }

    private static void Invoke(AutomationElement root, string name) =>
        ((InvokePattern)Find(root, name).GetCurrentPattern(InvokePattern.Pattern)).Invoke();

    private static string? ReadValue(AutomationElement root, string name) =>
        (Find(root, name).GetCurrentPattern(ValuePattern.Pattern) as ValuePattern)?.Current.Value;

    private static bool HasName(AutomationElement root, string name) => root.FindFirst(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.NameProperty, name)) is not null;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
