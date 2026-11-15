using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.Localization;
using DesktopMotionStudio.Core.Shortcuts;
using DesktopMotionStudio.Core.State;
using DesktopMotionStudio.Core.Video;

internal static class F22ImportChecks
{
    internal static int RunStore(string fixtures)
    {
        string studio = Environment.GetEnvironmentVariable("DMS_F08_STUDIO_PATH") ?? "";
        if (!File.Exists(studio)) return 2;
        var temp = Directory.CreateTempSubdirectory("dms-f22-import-");
        try
        {
            string profile = Path.Combine(temp.FullName, "isolated.json");
            string source = Path.Combine(temp.FullName, "原始素材.mp4");
            File.Copy(Path.Combine(fixtures, "landscape.mp4"), source);
            byte[] original = File.ReadAllBytes(source);
            var imports = ImportedVideoStore.ForProfile(profile);
            var inspector = new VideoInspector(Path.Combine(Path.GetDirectoryName(studio)!, "MotionHost", VideoPlaybackRequest.WorkerName));
            var ready = imports.PrepareAsync(source, inspector).GetAwaiter().GetResult();
            using var draft = ready.Value ?? throw new InvalidOperationException($"Preparation failed: {ready.Error}.");
            imports.RecoverDrafts();
            Require(imports.List().Count == 0 && File.Exists(draft.AssetPath), "Active draft is hidden and survives recovery.");
            Require(draft.Save(new("作品", 999, VideoFit.Complete, false)).Error == ImportError.InvalidOptions, "Reject duration under one second.");
            var saved = draft.Save(new("自定义作品", 1500, VideoFit.Fill, true));
            var work = saved.Value ?? throw new InvalidOperationException($"Save failed: {saved.Error}.");
            Require(imports.List().Count == 1 && File.ReadAllBytes(work.AssetPath).SequenceEqual(original) &&
                File.ReadAllBytes(source).SequenceEqual(original), "Source and owned copy remain byte-identical.");
            File.Move(source, Path.Combine(temp.FullName, "已移动.mp4"));
            imports = ImportedVideoStore.ForProfile(profile);
            Require(imports.Find(work.Id) == work, "A reopened profile can resolve the work without its source.");

            string target = Environment.GetEnvironmentVariable("DMS_F06_TARGET_PATH") ?? "";
            if (File.Exists(target))
            {
                var state = new StudioStateRepository(profile);
                Require(state.Save(StudioState.Empty with
                {
                    Applications =
                    [
                        new("first-app", "First", target, new(ApplicationTargetKind.Executable, target, [])),
                        new("second-app", "Second", target, new(ApplicationTargetKind.Executable, target, []))
                    ],
                    Bindings = [new("first-binding", "first-app", work.Id, true), new("second-binding", "second-app", work.Id, true)]
                }) == RepositoryError.None, "Save two bindings.");
                var requests = new ManagedLaunchRequest(state, Path.Combine(temp.FullName, "empty-scenes"), imports);
                Require(requests.Resolve("first-binding").Video == work.Clip &&
                    requests.Resolve("second-binding").Video == work.Clip, "Both launch plans resolve the stored copy.");
                Require(state.Save(state.Load().State! with { Settings = new("system", true) }) == RepositoryError.None &&
                    requests.Resolve("first-binding").Video?.Sound == false, "Global mute overrides saved original audio.");
            }
            var edited = imports.Edit(work.Id, work.Revision, new("Renamed 作品", 1000, VideoFit.Complete, false));
            Require(edited.Value is { Revision: 2 } updated && updated.Id == work.Id && updated.AssetPath == work.AssetPath &&
                imports.Find(work.Id) == updated, $"Edit persists one revision and stable ID ({edited.Error}).");
            Require(imports.Edit(work.Id, work.Revision, work.Options).Error == ImportError.Changed, "Reject stale revision.");
            string metadata = Path.Combine(Path.GetDirectoryName(work.AssetPath)!, "work.json");
            byte[] knownMetadata = File.ReadAllBytes(metadata);
            string unknown = File.ReadAllText(metadata).Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal);
            File.WriteAllText(metadata, unknown);
            Require(imports.Find(work.Id) is null && imports.Remove(work.Id, 2) == ImportError.Changed &&
                File.ReadAllText(metadata) == unknown, "Unknown metadata schema is unavailable and preserved.");
            File.WriteAllBytes(metadata, knownMetadata);
            using (File.Open(work.AssetPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                Require(imports.Remove(work.Id, 2) == ImportError.Busy && imports.Find(work.Id) is not null, "Reject removal while playing.");
            Require(imports.Remove(work.Id, 2) == ImportError.None && imports.Find(work.Id) is null && !File.Exists(work.AssetPath),
                "Verified removal clears the catalog and owned media.");
            if (File.Exists(target))
            {
                var state = new StudioStateRepository(profile);
                var fallback = new ManagedLaunchRequest(state, temp.FullName, imports).Resolve("first-binding");
                Require(fallback is { Status: ManagedLaunchStatus.Ready, Video: null } &&
                    state.Load().State!.Bindings.Count == 2, "Removal preserves bindings and resolves a native launch.");
            }
            var invalid = imports.PrepareAsync(Path.GetFullPath(Path.Combine(fixtures, "malformed.mp4")), inspector).GetAwaiter().GetResult();
            Require(invalid.Error == ImportError.InvalidMedia && imports.List().Count == 0, "Invalid media leaves no catalog entry.");
            Require(imports.PrepareAsync(@"\\invalid\share\clip.mp4", inspector).GetAwaiter().GetResult().Error == ImportError.UnsafePath,
                "Network paths are rejected before inspection.");
            string longClip = Path.GetFullPath(Path.Combine(fixtures, "landscape-10s.mp4"));
            var pending = imports.PrepareAsync(longClip, inspector).GetAwaiter().GetResult();
            using (var canceled = pending.Value ?? throw new InvalidOperationException($"Preparation failed: {pending.Error}."))
            {
                using var cancel = new CancellationTokenSource();
                cancel.Cancel();
                bool stopped = false;
                try { canceled.Save(new("Canceled", 10_000, VideoFit.Complete, false), cancel.Token); }
                catch (OperationCanceledException) { stopped = true; }
                Require(stopped && imports.List().Count == 0, "Canceling save cannot publish a draft.");
            }
            Require(!Directory.EnumerateFileSystemEntries(profile + ".motions").Any(), "All test drafts and removed work are cleaned.");
            Console.WriteLine("PASS F22 import, source move, reload, stable edit, busy removal, native fallback and invalid media.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { temp.Delete(recursive: true); }
    }

    internal static int RunUi(string fixtures)
    {
        string studio = Environment.GetEnvironmentVariable("DMS_F08_STUDIO_PATH") ?? "";
        if (!File.Exists(studio)) return 2;
        var temp = Directory.CreateTempSubdirectory("dms-f22-ui-");
        string profile = Path.Combine(temp.FullName, "isolated.json");
        string catalog = Path.Combine(Path.GetDirectoryName(studio)!, "Scenes");
        string worker = Path.Combine(Path.GetDirectoryName(studio)!, "MotionHost", VideoPlaybackRequest.WorkerName);
        var imports = ImportedVideoStore.ForProfile(profile);
        try
        {
            Require(new StudioStateRepository(profile).Save(StudioState.Empty) == RepositoryError.None, "Could not create test profile.");
            using (var process = StudioUiChecks.Start(studio, profile, catalog))
            {
                try
                {
                    var library = OpenLibrary(process, "打开动效作品库", "动效作品库");
                    Toggle(library, "我的动效").Toggle();
                    Require(Program.WaitFor(() => Exists(library, "导入一个本地 MP4 即可开始。")), "Missing imported empty state.");
                    var editor = OpenEditor(process, library, Path.GetFullPath(Path.Combine(fixtures, "landscape.mp4")), "导入 MP4");
                    Require(Program.WaitFor(() => StudioUiChecks.Find(editor, "保存动效").Current.IsEnabled), "Valid file did not enable save.");
                    var duration = EditorValue(editor, "ImportDuration");
                    Require(duration.Current.Value == "6", "Unexpected default duration.");
                    EditorValue(editor, "ImportWorkName").SetValue("测试动效・東京");
                    var sound = Toggle(editor, "播放原声");
                    Require(sound.Current.ToggleState == ToggleState.Off, "Audio default should be off.");
                    duration.SetValue("3");
                    sound.Toggle();
                    var fit = ById(editor, "ImportFit");
                    ((ExpandCollapsePattern)fit.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
                    ((SelectionItemPattern)StudioUiChecks.Find(fit, "居中填充").GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                    F09UiChecks.Invoke(editor, "预览作品");
                    nint stage = 0;
                    Require(Program.WaitFor(() => (stage = StudioUiChecks.FindOwnedMotionWindow(worker,
                        "Desktop Motion Studio — Video preview")) != 0), "Draft preview did not show.");
                    StudioUiChecks.Keys(stage, 0x1B);
                    F09UiChecks.Invoke(editor, "保存动效");
                    Require(Program.WaitFor(() => imports.List() is
                        [{ Options: { Name: "测试动效・東京", DurationMilliseconds: 3000, Sound: true, Fit: VideoFit.Fill } }]),
                        "Saving from the editor did not publish the expected work.");
                    Require(Program.WaitFor(() => Exists(library, "测试动效・東京")), "Saved work did not appear in My motions.");
                }
                finally { StudioUiChecks.Stop(process); }
            }
            Require(new StudioLanguagePreference(profile).TrySave("en"), "Could not save English language.");
            using (var process = StudioUiChecks.Start(studio, profile, catalog))
            {
                try
                {
                    var library = OpenLibrary(process, "Open motion library", "Motion library");
                    Toggle(library, "My motions").Toggle();
                    Require(Program.WaitFor(() => Exists(library, "测试动效・東京")), "The imported name changed on restart or language switch.");
                    var editor = OpenEditor(process, library, Path.GetFullPath(Path.Combine(fixtures, "malformed.mp4")), "Import MP4");
                    Require(Program.WaitFor(() => Exists(editor, "The file could not be decoded. Use H.264 MP4 with AAC audio or no audio.")),
                        "Invalid file did not show the localized error.");
                    Require(!StudioUiChecks.Find(editor, "Save motion").Current.IsEnabled, "Invalid media allowed saving.");
                    F09UiChecks.Invoke(editor, "Cancel");
                }
                finally { StudioUiChecks.Stop(process); }
            }
            Console.WriteLine("PASS F22 Windows editor import, draft preview, save, restart, preserved custom name and English error.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { temp.Delete(recursive: true); }
    }

    private static AutomationElement OpenLibrary(Process process, string action, string title)
    {
        var home = AutomationElement.FromHandle(StudioUiChecks.Window(process, "Desktop Motion Studio"));
        F09UiChecks.Invoke(home, action);
        return AutomationElement.FromHandle(StudioUiChecks.Window(process, title));
    }

    private static AutomationElement OpenEditor(Process process, AutomationElement library, string path, string title)
    {
        F09UiChecks.Invoke(library, title);
        string filename = title == "Import MP4" ? "File name:" : "文件名:";
        nint dialog = StudioUiChecks.Window(process, title);
        var picker = AutomationElement.FromHandle(dialog);
        var edit = picker.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1148"));
        if (edit is null) throw new InvalidOperationException($"File dialog input not found ({filename}).");
        var input = edit.TryGetCurrentPattern(ValuePattern.Pattern, out object value) ? (ValuePattern)value :
            (ValuePattern)edit.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty,
                ControlType.Edit)).GetCurrentPattern(ValuePattern.Pattern);
        input.SetValue(path);
        StudioUiChecks.Keys(dialog, 0x0D);
        nint editor = 0;
        Require(Program.WaitFor(() => (editor = StudioUiChecks.FindWindow(process.Id, title)) != 0 && editor != dialog),
            "The import editor did not open after file selection.");
        return AutomationElement.FromHandle(editor);
    }

    private static bool Exists(AutomationElement parent, string name) => parent.FindFirst(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.NameProperty, name)) is not null;

    private static TogglePattern Toggle(AutomationElement parent, string name) =>
        (TogglePattern)(parent.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.NameProperty, name),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox)))
            ?? throw new InvalidOperationException($"Check box missing: {name}")).GetCurrentPattern(TogglePattern.Pattern);

    private static ValuePattern EditorValue(AutomationElement editor, string id) =>
        (ValuePattern)ById(editor, id).GetCurrentPattern(ValuePattern.Pattern);

    private static AutomationElement ById(AutomationElement editor, string id) =>
        editor.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id))
        ?? throw new InvalidOperationException($"Editor field missing: {id}");

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }

    internal static int RunInspection(string fixtures)
    {
        string studio = Environment.GetEnvironmentVariable("DMS_F08_STUDIO_PATH") ?? "";
        if (!File.Exists(studio)) return 2;
        string worker = Path.Combine(Path.GetDirectoryName(studio)!, "MotionHost", VideoPlaybackRequest.WorkerName);
        var inspector = new VideoInspector(worker);
        foreach (var (name, error, duration, audio) in new[]
        {
            ("landscape.mp4", VideoInspectionError.None, 6000, true),
            ("portrait.mp4", VideoInspectionError.None, 6000, false),
            ("malformed.mp4", VideoInspectionError.InvalidMedia, 0, false),
            ("unsupported.mp4", VideoInspectionError.InvalidMedia, 0, false)
        })
        {
            string path = Path.GetFullPath(Path.Combine(fixtures, name));
            if (!File.Exists(path)) throw new FileNotFoundException("Missing F22 test fixture.", path);
            var timer = Stopwatch.StartNew();
            Task<VideoInspectionResult> pending = inspector.InspectAsync(path);
            while (!pending.IsCompleted)
            {
                if (StudioUiChecks.FindOwnedMotionWindow(worker, "Desktop Motion Studio — Video preview") != 0 ||
                    StudioUiChecks.FindOwnedMotionWindow(worker, "Desktop Motion Studio — Video launch") != 0)
                    throw new InvalidOperationException("Inspecting a file showed a playback stage.");
                Thread.Sleep(15);
            }
            VideoInspectionResult result = pending.GetAwaiter().GetResult();
            if (result.Error != error || Math.Abs(result.DurationMilliseconds - duration) > 30 || result.HasAudio != audio)
                throw new InvalidOperationException($"Unexpected inspection for {name}: {result}");
            Console.WriteLine($"PASS F22 inspect {name}: {result.Error}, duration={result.DurationMilliseconds} ms, audio={result.HasAudio}, elapsed={timer.ElapsedMilliseconds} ms.");
        }
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try { inspector.InspectAsync(Path.GetFullPath(Path.Combine(fixtures, "landscape.mp4")), canceled.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { Console.WriteLine("PASS F22 canceled inspection."); return 0; }
        throw new InvalidOperationException("Canceled inspection proceeded.");
    }
}
