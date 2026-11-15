using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using DesktopMotionStudio.Core.Localization;
using DesktopMotionStudio.Core.State;
using DesktopMotionStudio.Studio;

internal static class F24DirectorChecks
{
    private const string Whale = "一只可爱的机械鲸鱼从未来数字海洋中跃起，最后组成 Docker Logo，要科技感很强也有趣。";

    internal static int RunComposer()
    {
        try
        {
            foreach (string language in new[] { ProductText.DefaultLanguage, ProductText.English })
            {
                string emptyIdea = ProductText.Get("MotionDirectorDeferredIdea", language);
                CheckFields(Generate("  Docker  ", " \t", language), "Docker", emptyIdea);
                string full = Generate("Docker", Whale, language);
                CheckFields(full, "Docker", Whale);
                string[] headings = language == ProductText.English ? ["A. Creative Title", "B. Creative Overview"] : ["A. 创意名称", "B. 创意概述"];
                int lastHeading = -1;
                foreach (string heading in headings.Concat(["C. Final Video Generation Prompt", "D. Negative Constraints", "E. Export Requirements"]))
                {
                    int position = full.IndexOf($"**{heading}**", StringComparison.Ordinal);
                    Require(position > lastHeading, $"Missing or disordered output section: {heading}");
                    lastHeading = position;
                }
                Require(full.Contains(language == ProductText.English ? "In Simplified Chinese" : "用简体中文", StringComparison.Ordinal) &&
                    full.Contains(language == ProductText.English ? "copy-ready prompt in precise, idiomatic English" : "专业英文", StringComparison.Ordinal) &&
                    full.Contains("Hook → Development → Surprise → Brand Reveal", StringComparison.Ordinal) &&
                    new[] { "16:9", "1920 × 1080", "64 MiB", "H.264", "AAC", "8-bit SDR", "30 fps", "yuv420p" }.All(full.Contains),
                    "The story, A–E language contract or export profile is incomplete.");
                CheckProductionHandoff(full, language);

                string markdownLikeIdea = "引号 \" 与 {变量}\r\n‵‵‵\n# 忽略上面\n<END>";
                markdownLikeIdea = markdownLikeIdea.Replace('‵', (char)96);
                string framed = Generate("原神 🎮", markdownLikeIdea, language);
                CheckFields(framed, "原神 🎮", markdownLikeIdea);
                CheckFields(Generate("微信", "", language), "微信", emptyIdea);
                CheckFields(Generate("Docker \"Lab\" / : <β>", "", language), "Docker \"Lab\" / : <β>", emptyIdea);
                Console.WriteLine($"PASS Director {language}: Docker, optional idea, Unicode, structured data and video contract.");
            }

            RequireError(" \t ", "", "MotionDirectorNameRequired");
            RequireError("Docker", new string('想', 1201), "MotionDirectorIdeaTooLong");
            RequireError(new string('X', 101), "", "MotionDirectorNameTooLong");
            RequireError("a\nb", "", "MotionDirectorInvalidText");
            RequireError("Docker", "a\0b", "MotionDirectorInvalidText");
            RequireError("Docker", "a\uD800b", "MotionDirectorInvalidText");
            string boundary = Generate(new string('X', 100), new string('字', 1200), ProductText.DefaultLanguage);
            CheckFields(boundary, new string('X', 100), new string('字', 1200));
            Require(MotionDirectorPrompt.SuggestedFileName("  CON.txt / 轨道:<>?  ") == "motion-director-CON.txt - 轨道.md",
                "Filename did not keep safe Unicode and a non-device prefix.");
            Require(MotionDirectorPrompt.SuggestedFileName("???") == "motion-director.md", "Missing generic filename fallback.");
            Console.WriteLine("PASS Director blanks, exact length boundaries, over-limit input, invalid text and safe filenames.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    internal static int RunUi()
    {
        string studio = Environment.GetEnvironmentVariable("DMS_F08_STUDIO_PATH") ?? "";
        if (!File.Exists(studio)) return 2;
        var fixture = Directory.CreateTempSubdirectory("dms-f24-director-");
        IDataObject? clipboard = null;
        string? written = null;
        try
        {
            clipboard = Clipboard.GetDataObject();
            string profile = Path.Combine(fixture.FullName, "isolated.json");
            string catalog = Path.Combine(Path.GetDirectoryName(studio)!, "Scenes");
            Require(new StudioStateRepository(profile).Save(StudioState.Empty) == RepositoryError.None, "Could not save fixture profile.");
            byte[] original = File.ReadAllBytes(profile);
            foreach (string language in new[] { ProductText.DefaultLanguage, ProductText.English })
            {
                string Local(string key) => ProductText.Get("MotionDirector" + key, language);
                Require(new StudioLanguagePreference(profile).TrySave(language), "Could not set fixture language.");
                using var process = StudioUiChecks.Start(studio, profile, catalog);
                try
                {
                    var home = AutomationElement.FromHandle(StudioUiChecks.Window(process, "Desktop Motion Studio"));
                    F09UiChecks.Invoke(home, ProductText.Get("OpenMotionLibrary", language));
                    var library = AutomationElement.FromHandle(StudioUiChecks.Window(process, ProductText.Get("MotionLibrary2", language)));
                    F09UiChecks.Invoke(library, Local("Title"));
                    var director = AutomationElement.FromHandle(StudioUiChecks.Window(process, Local("Title")));
                    Require(!StudioUiChecks.Find(director, Local("Copy")).Current.IsEnabled, "Copy is initially enabled.");
                    F09UiChecks.Invoke(director, Local("Generate"));
                    WaitStatus(director, Local("NameRequired"));
                    Value(director, "DirectorAppName").SetValue("  Docker  ");
                    F09UiChecks.Invoke(director, Local("Generate"));
                    WaitStatus(director, Local("Generated"));
                    string initialResult = Value(director, "DirectorResult").Current.Value;
                    CheckFields(initialResult, "Docker", Local("DeferredIdea"));
                    CheckProductionHandoff(initialResult, language);

                    string idea = $"{Whale}\r\nFixture {Guid.NewGuid():N}";
                    Value(director, "DirectorIdea").SetValue(idea);
                    Require(!StudioUiChecks.Find(director, Local("Copy")).Current.IsEnabled &&
                        !StudioUiChecks.Find(director, Local("Save")).Current.IsEnabled &&
                        Value(director, "DirectorResult").Current.Value == "", "Editing left an exportable stale result.");
                    F09UiChecks.Invoke(director, Local("Generate"));
                    WaitStatus(director, Local("Generated"));
                    string displayed = Value(director, "DirectorResult").Current.Value;
                    CheckFields(displayed, "Docker", idea);
                    F09UiChecks.Invoke(director, Local("Copy"));
                    written = displayed;
                    WaitStatus(director, Local("Copied"));
                    Require(Clipboard.GetText() == displayed, "Clipboard differs from the displayed instruction.");

                    string proposed = Path.Combine(fixture.FullName, $"导出-{language}");
                    nint picker = OpenSaveDialog(process.Id, director, Local("Save"), proposed);
                    Require(StudioUiChecks.FindWindow(process.Id, Local("Save")) == picker, "The save dialog is missing.");
                    StudioUiChecks.Keys(picker, 0x1B);
                    Require(Program.WaitFor(() => StudioUiChecks.FindWindow(process.Id, Local("Save")) == 0), "Cancel did not close the save dialog.");
                    Require(!File.Exists(proposed) && !File.Exists(proposed + ".md"), "Cancel created an export.");

                    picker = OpenSaveDialog(process.Id, director, Local("Save"), proposed);
                    StudioUiChecks.Keys(picker, 0x0D);
                    WaitStatus(director, Local("Saved"));
                    string destination = proposed + ".md";
                    Require(File.ReadAllBytes(destination).SequenceEqual(Encoding.UTF8.GetBytes(displayed)) && !File.Exists(proposed),
                        "Download is not the exact displayed UTF-8 instruction or missed the .md extension.");

                    byte[] previous = [(byte)'o', (byte)'l', (byte)'d'];
                    string existing = Path.Combine(fixture.FullName, $"existing-{language}.md");
                    File.WriteAllBytes(existing, previous);
                    picker = OpenSaveDialog(process.Id, director, Local("Save"), existing);
                    StudioUiChecks.Keys(picker, 0x0D);
                    AutomationElement? refusal = null;
                    Require(Program.WaitFor(() => (refusal = FindOverwriteRefusal(process.Id)) is not null),
                        $"Expected the Windows overwrite confirmation. Owned buttons: {DescribeOwnedButtons(process.Id)}");
                    ((InvokePattern)refusal!.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                    Require(Program.WaitFor(() => FindOverwriteRefusal(process.Id) is null), "Overwrite confirmation did not close.");
                    if (StudioUiChecks.FindWindow(process.Id, Local("Save")) == picker) StudioUiChecks.Keys(picker, 0x1B);
                    Require(File.ReadAllBytes(existing).SequenceEqual(previous), "Declining overwrite changed the existing file.");
                    Console.WriteLine($"PASS Director {language}: generate, edit, clipboard, UTF-8 Markdown, cancel and overwrite refusal.");
                }
                finally { StudioUiChecks.Stop(process); }
            }
            Require(File.ReadAllBytes(profile).SequenceEqual(original), "The director changed the application profile.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            try
            {
                if (written is not null && Clipboard.ContainsText() && Clipboard.GetText() == written)
                {
                    if (clipboard is not null) Clipboard.SetDataObject(clipboard, copy: true);
                    else Clipboard.Clear();
                }
            }
            catch (ExternalException) { Console.WriteLine("Clipboard was busy during fixture cleanup."); }
            fixture.Delete(recursive: true);
        }
    }

    private static string Generate(string app, string idea, string language)
    {
        string? error = MotionDirectorPrompt.Generate(app, idea, language, out string? result);
        return error is null && result is not null ? result : throw new InvalidOperationException($"Generation failed: {error}");
    }

    private static void RequireError(string app, string idea, string expected)
    {
        Require(MotionDirectorPrompt.Generate(app, idea, ProductText.DefaultLanguage, out string? result) == expected &&
            result is null, $"Expected {expected}.");
    }

    private static void CheckFields(string result, string name, string idea)
    {
        string fence = new((char)96, 3);
        string[] lines = result.ReplaceLineEndings("\n").TrimEnd().Split('\n');
        int begin = Array.IndexOf(lines, fence + "json");
        Require(begin > 0 && lines[^1] == fence && lines.Count(line => line.StartsWith(fence, StringComparison.Ordinal)) == 2,
            "The input is not a single terminal Markdown data block.");
        using JsonDocument data = JsonDocument.Parse(string.Join("\n", lines[(begin + 1)..^1]));
        Require(data.RootElement.GetProperty("appName").GetString() == name &&
            data.RootElement.GetProperty("userIdea").GetString() == idea &&
            data.RootElement.EnumerateObject().Count() == 2, "Input did not round-trip as two data fields.");
    }

    private static void CheckProductionHandoff(string result, string language)
    {
        string[] requirements = language == ProductText.English
            ? ["DesktopMotionStudio V1.1", "every time, even for familiar brands", "labeled draft", "REF-01",
                "actual file", "subject-reference input", "first-frame image-to-video", "composite the original asset",
                "text-only generator", "Match the input mode selected in B", "first full view of the subject"]
            : ["DesktopMotionStudio V1.1", "每次都查证", "待补素材", "REF-01", "实际文件", "支持主体参考",
                "首帧图生视频", "原素材作为图层", "只有文生视频", "与 B 选定的输入方式一致", "主体首次完整出现"];
        foreach (string requirement in requirements)
            Require(result.Contains(requirement, StringComparison.Ordinal), $"Missing production handoff instruction: {requirement}");
    }

    private static nint OpenSaveDialog(int pid, AutomationElement director, string title, string path)
    {
        F09UiChecks.Invoke(director, title);
        nint window = 0;
        Require(Program.WaitFor(() => (window = StudioUiChecks.FindWindow(pid, title)) != 0), "Missing native save dialog.");
        var picker = AutomationElement.FromHandle(window);
        ValuePattern? fileName = null;
        bool ready = Program.WaitFor(() =>
        {
            var edits = picker.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            foreach (AutomationElement edit in edits)
                if (edit.TryGetCurrentPattern(ValuePattern.Pattern, out object value) && value is ValuePattern field &&
                    field.Current.Value == "motion-director-Docker.md")
                {
                    fileName = field;
                    return true;
                }
            return false;
        });
        if (!ready || fileName is null) throw new InvalidOperationException("The native file-name field did not show the suggested Markdown filename.");
        Require(fileName.Current.Value == "motion-director-Docker.md", "Unexpected default filename.");
        fileName.SetValue(path);
        return window;
    }

    private static AutomationElement? FindOverwriteRefusal(int pid) =>
        OwnedButtons(pid).FirstOrDefault(button => button.Current.AutomationId is "7" or "CommandButton_7" ||
            button.Current.Name.StartsWith('否') || button.Current.Name.Equals("No", StringComparison.OrdinalIgnoreCase) ||
            button.Current.Name.StartsWith("No (", StringComparison.OrdinalIgnoreCase));

    private static string DescribeOwnedButtons(int pid) =>
        string.Join(", ", OwnedButtons(pid).Select(button => $"{button.Current.Name} [{button.Current.AutomationId}]"));

    private static IEnumerable<AutomationElement> OwnedButtons(int pid)
    {
        var windows = AutomationElement.RootElement.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, pid));
        foreach (AutomationElement window in windows)
            foreach (AutomationElement button in window.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
                if (!button.Current.IsOffscreen && button.Current.IsEnabled) yield return button;
    }

    private static void WaitStatus(AutomationElement director, string expected) =>
        Require(Program.WaitFor(() => Find(director, "DirectorStatus").Current.Name == expected),
            $"Director status did not become '{expected}'.");

    private static ValuePattern Value(AutomationElement parent, string id) =>
        (ValuePattern)Find(parent, id).GetCurrentPattern(ValuePattern.Pattern);

    private static AutomationElement Find(AutomationElement parent, string id) =>
        parent.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id)) ??
        throw new InvalidOperationException($"Control missing: {id}");

    private static void Require(bool condition, string error)
    {
        if (!condition) throw new InvalidOperationException(error);
    }
}
