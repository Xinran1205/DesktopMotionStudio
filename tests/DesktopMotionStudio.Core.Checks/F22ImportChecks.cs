using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using DesktopMotionStudio.Core.IO;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.Video;

internal static class F22ImportChecks
{
    internal static int Run()
    {
        var root = Directory.CreateTempSubdirectory("dms-f22-");
        try { return Run(root.FullName); }
        finally { root.Delete(recursive: true); }
    }

    private static int Run(string root)
    {
        using var scope = WindowsFileScope.Open(root);
        byte[] partial = [1, 2, 3];
        using (var stage = scope.StageDirectory(".draft-interrupted"))
        {
            Write(stage.Scope, "clip.mp4", partial);
            Require(!Directory.Exists(scope.Child("published")), "Partial copy became visible as a work.");
        }
        Require(!Directory.Exists(scope.Child(".draft-interrupted")), "Interrupted copy left behind owned files.");

        using (var stage = scope.StageDirectory(".draft-publish"))
        {
            Write(stage.Scope, "clip.mp4", partial);
            Write(stage.Scope, "work.json", [4, 5]);
            stage.Publish("published");
        }
        string published = Path.Combine(scope.Child("published"), "clip.mp4");
        Require(File.ReadAllBytes(published).SequenceEqual(partial), "Published bytes differ from the draft.");
        using (var stage = scope.StageDirectory(".draft-collision"))
        {
            Write(stage.Scope, "clip.mp4", [9]);
            bool rejected = false;
            try { stage.Publish("published"); }
            catch (Win32Exception) { rejected = true; }
            Require(rejected, "Publication replaced a pre-existing work.");
        }
        Require(File.ReadAllBytes(published).SequenceEqual(partial), "A failed publish changed the previous work.");

        using (var stage = scope.StageDirectory(".draft-foreign"))
        {
            Write(stage.Scope, "clip.mp4", partial);
            File.Move(stage.Scope.Child("clip.mp4"), stage.Scope.Child("retained.mp4"));
            File.WriteAllBytes(stage.Scope.Child("clip.mp4"), [7]);
        }
        string foreign = Path.Combine(scope.Child(".draft-foreign"), "clip.mp4");
        Require(File.ReadAllBytes(foreign).SequenceEqual(new byte[] { 7 }), "Cleanup touched a replacement file.");

        string crashRoot = Directory.CreateDirectory(Path.Combine(root, "interrupted")).FullName;
        string app = Path.ChangeExtension(typeof(F22ImportChecks).Assembly.Location, ".exe");
        var start = new ProcessStartInfo(app) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("--f22-interrupt-worker");
        start.ArgumentList.Add(crashRoot);
        using var worker = Process.Start(start) ?? throw new IOException("Could not start import fixture.");
        try
        {
            Require(worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult() == "copied",
                "Interrupted import fixture did not create its private copy.");
            var imports = new ImportedVideoStore(crashRoot);
            imports.RecoverDrafts();
            Require(imports.List().Count == 0 && Directory.EnumerateDirectories(crashRoot).Count() == 1,
                "Recovery hid the draft or removed it while its owner was alive.");
            worker.Kill();
            Require(worker.WaitForExit(5000), "Interrupted fixture did not exit.");
            imports.RecoverDrafts();
            Require(!Directory.EnumerateFileSystemEntries(crashRoot).Any(), "Recovery did not remove the interrupted import.");
        }
        finally { if (!worker.HasExited) { worker.Kill(); worker.WaitForExit(5000); } }
        Console.WriteLine("PASS F22 private staging, killed import process, atomic publish, collision, and identity-based cleanup.");
        return 0;
    }

    internal static int RunWorker(string root)
    {
        if (Path.GetFileName(Path.GetDirectoryName(root))?.StartsWith("dms-f22-", StringComparison.Ordinal) != true ||
            Path.GetFileName(root) != "interrupted") return 2;
        using var scope = WindowsFileScope.Open(root);
        string id = ImportedVideoWork.Prefix + Guid.NewGuid().ToString("N");
        using var stage = scope.StageDirectory(".draft-" + id);
        Write(stage.Scope, "clip.mp4", [1, 2, 3]);
        ImportFileIdentity identity;
        using (var media = stage.Scope.OpenChild("clip.mp4", WindowsFileScope.ReadAccess))
            identity = ImportFileIdentity.Of(WindowsFileScope.ReadInfo(media));
        Write(stage.Scope, "draft.json", JsonSerializer.SerializeToUtf8Bytes(new { SchemaVersion = 1, Id = id, Media = identity }, SceneDataJson.Options));
        Console.WriteLine("copied");
        Thread.Sleep(TimeSpan.FromSeconds(15));
        return 3;
    }

    private static void Write(WindowsFileScope scope, string name, byte[] bytes)
    {
        using var handle = scope.OpenChild(name, WindowsFileScope.WriteAccess, disposition: WindowsFileScope.CreateNew);
        RandomAccess.Write(handle, bytes, 0);
        RandomAccess.FlushToDisk(handle);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

}
