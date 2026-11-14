using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using DesktopMotionStudio.Core.Localization;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.State;
using DesktopMotionStudio.Core.Video;

internal static class F23OrbitChecks
{
    internal static int Run()
    {
        string studio = Environment.GetEnvironmentVariable("DMS_F08_STUDIO_PATH") ?? "";
        if (!File.Exists(studio)) return 2;
        string catalog = Path.Combine(Path.GetDirectoryName(studio)!, "Scenes");
        string worker = Path.Combine(Path.GetDirectoryName(studio)!, "MotionHost", VideoPlaybackRequest.WorkerName);
        var fixture = Directory.CreateTempSubdirectory("dms-f23-");
        try
        {
            var item = new SceneLibrary().Scan(catalog).Items.Single(scene => scene.Catalog.Manifest?.SceneId == "orbit-ignition");
            Require(item is { IsPlayable: true, CoverError: SceneAssetError.None, Video: { Sound: false, DurationMilliseconds: 6000 } },
                "Bundled video or cover is unavailable.");
            var inspector = new VideoInspector(worker);
            Require(inspector.InspectAsync(item.Video!.AssetPath).GetAwaiter().GetResult() is
                { Error: VideoInspectionError.None, DurationMilliseconds: 6000, HasAudio: false }, "Shipped media did not match the F22 contract.");
            string profile = Path.Combine(fixture.FullName, "isolated.json");
            Require(new StudioStateRepository(profile).Save(StudioState.Empty) == RepositoryError.None, "Could not save isolated settings.");
            var imported = ImportedVideoStore.ForProfile(profile).PrepareAsync(item.Video.AssetPath, inspector).GetAwaiter().GetResult();
            using (var draft = imported.Value)
                Require(draft?.Save(new("Orbit import verification", 6000, VideoFit.Complete, false)).Error == ImportError.None,
                    "Could not import the shipped video.");
            Console.WriteLine("PASS F23 exact packaged MP4 and cover accepted, six seconds, silent, imported through F22.");
            foreach (bool english in new[] { false, true })
            {
                string language = english ? ProductText.English : ProductText.DefaultLanguage;
                Require(new StudioLanguagePreference(profile).TrySave(language), "Could not set fixture language.");
                Require(new MotionDisplayPreference(profile).TrySave(english ? "compact" : "recommended"),
                    "Could not set the fixture display preference.");
                using var process = StudioUiChecks.Start(studio, profile, catalog);
                try
                {
                    var home = AutomationElement.FromHandle(StudioUiChecks.Window(process, "Desktop Motion Studio"));
                    F09UiChecks.Invoke(home, ProductText.Get("OpenMotionLibrary", language));
                    var library = AutomationElement.FromHandle(StudioUiChecks.Window(process, ProductText.Get("MotionLibrary2", language)));
                    string list = ProductText.Get("CompleteMotionWorks", language);
                    Require(Program.WaitFor(() => F09UiChecks.ListCount(library, list) >= 3), "Bundled works did not load.");
                    F09UiChecks.Select(library, list, ProductText.Get("OrbitIgnitionName", language));
                    string description = ProductText.Get("OrbitIgnitionDescription", language);
                    Require(Program.WaitFor(() => library.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.NameProperty, description)) is not null), "Localized description missing.");
                    F09UiChecks.Invoke(library, ProductText.Get("PreviewWork2", language));
                    nint stage = 0;
                    Require(Program.WaitFor(() => (stage = StudioUiChecks.FindOwnedMotionWindow(worker,
                        "Desktop Motion Studio — Video preview")) != 0), "No video preview.");
                    F18DisplayChecks.VerifyStageBounds(stage, full: !english);
                    bool focused = true;
                    try { StudioUiChecks.Keys(stage, 0x1B); }
                    catch (InvalidOperationException) when (StudioUiChecks.GetForegroundWindow() != stage)
                    {
                        focused = false;
                        Require(PostMessageW(stage, 0x0100, 0x1B, 1), "Could not send Esc to the verified worker window.");
                    }
                    Require(Program.WaitFor(() => StudioUiChecks.FindOwnedMotionWindow(worker,
                        "Desktop Motion Studio — Video preview") == 0), "Escape did not close the preview.");
                    Console.WriteLine($"PASS F23 {language} title and description, {(english ? "compact" : "full-screen")} preview and Esc ({(focused ? "focused keyboard" : "owned window message")}).");
                }
                finally { StudioUiChecks.Stop(process); }
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { fixture.Delete(recursive: true); }
    }

    private static void Require(bool condition, string error)
    {
        if (!condition) throw new InvalidOperationException(error);
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nuint key, nint flags);
}
