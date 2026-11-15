using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using DesktopMotionStudio.Core.Localization;
using DesktopMotionStudio.Core.State;

internal static class F24GuideChecks
{
    internal static int Run()
    {
        string studio = Environment.GetEnvironmentVariable("DMS_F08_STUDIO_PATH") ?? "";
        if (!File.Exists(studio)) return 2;
        var fixture = Directory.CreateTempSubdirectory("dms-f24-");
        string concept = $"Comet 追光 {Guid.NewGuid():N}";
        string first = (8 * .28m).ToString("0.#", CultureInfo.CurrentCulture);
        string second = (8 * .74m).ToString("0.#", CultureInfo.CurrentCulture);
        IDataObject? clipboard = null;
        string? writtenBrief = null;
        try
        {
            clipboard = Clipboard.GetDataObject();
            string profile = Path.Combine(fixture.FullName, "isolated.json");
            string catalog = Path.Combine(Path.GetDirectoryName(studio)!, "Scenes");
            Require(new StudioStateRepository(profile).Save(StudioState.Empty) == RepositoryError.None, "Could not save test settings.");
            byte[] original = File.ReadAllBytes(profile);
            foreach (string language in new[] { ProductText.DefaultLanguage, ProductText.English })
            {
                Require(new StudioLanguagePreference(profile).TrySave(language), "Could not save fixture language.");
                using var process = StudioUiChecks.Start(studio, profile, catalog);
                try
                {
                    var home = AutomationElement.FromHandle(StudioUiChecks.Window(process, "Desktop Motion Studio"));
                    F09UiChecks.Invoke(home, ProductText.Get("OpenMotionLibrary", language));
                    var library = AutomationElement.FromHandle(StudioUiChecks.Window(process, ProductText.Get("MotionLibrary2", language)));
                    string title = ProductText.Get("MotionGuideTitle", language);
                    F09UiChecks.Invoke(library, title);
                    var guide = AutomationElement.FromHandle(StudioUiChecks.Window(process, title));
                    Value(guide, "GuideIdea").SetValue(concept);
                    Choose(guide, "GuideStyle", ProductText.Get("MotionGuideAnime", language));
                    Choose(guide, "GuideDuration", "8");
                    string brief = Value(guide, "GuideBrief").Current.Value;
                    Require(brief.Contains(concept, StringComparison.Ordinal) && brief.Contains(ProductText.Get("MotionGuideAnime", language), StringComparison.Ordinal) &&
                        brief.Contains("H.264", StringComparison.Ordinal) && brief.Contains("AAC", StringComparison.Ordinal) &&
                        brief.Contains("64 MiB", StringComparison.Ordinal) && brief.Contains($"0–{first}", StringComparison.Ordinal) &&
                        brief.Contains($"{second}–8", StringComparison.Ordinal), "The live brief does not reflect input or supported export settings.");
                    F09UiChecks.Invoke(guide, ProductText.Get("MotionGuideCopy", language));
                    writtenBrief = brief;
                    Require(Program.WaitFor(() => Find(guide, "GuideCopyStatus").Current.Name == ProductText.Get("MotionGuideCopied", language)),
                        "Copy did not report success.");
                    Require(Clipboard.GetText() == brief, "The clipboard differs from the displayed brief.");
                    Console.WriteLine($"PASS F24 {language}: live concept, style, duration, export guidance and clipboard.");
                }
                finally { StudioUiChecks.Stop(process); }
            }
            Require(File.ReadAllBytes(profile).SequenceEqual(original), "Guide changed the app profile.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            try
            {
                if (writtenBrief is not null && Clipboard.ContainsText() && Clipboard.GetText() == writtenBrief)
                {
                    if (clipboard is not null) Clipboard.SetDataObject(clipboard, copy: true);
                    else Clipboard.Clear();
                }
            }
            catch (ExternalException) { Console.WriteLine("Clipboard was busy during fixture cleanup."); }
            fixture.Delete(recursive: true);
        }
    }

    private static void Choose(AutomationElement guide, string id, string value)
    {
        var box = Find(guide, id);
        var expand = (ExpandCollapsePattern)box.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        expand.Expand();
        var option = StudioUiChecks.Find(box, value);
        ((SelectionItemPattern)option.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        expand.Collapse();
    }

    private static ValuePattern Value(AutomationElement guide, string id) =>
        (ValuePattern)Find(guide, id).GetCurrentPattern(ValuePattern.Pattern);

    private static AutomationElement Find(AutomationElement guide, string id) =>
        guide.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id))
        ?? throw new InvalidOperationException($"Guide control missing: {id}");

    private static void Require(bool condition, string error)
    {
        if (!condition) throw new InvalidOperationException(error);
    }
}
