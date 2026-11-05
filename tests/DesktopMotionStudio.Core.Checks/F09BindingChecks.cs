using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.Bindings;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.State;

internal static class F09BindingChecks
{
    internal static int Run()
    {
        int failures = 0;
        void Check(string name, bool passed) { Console.WriteLine($"{(passed ? "PASS" : "FAIL")} F09 eligibility {name}"); if (!passed) failures++; }
        string root = Path.Combine(Path.GetTempPath(), $"dms-f09-binding-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        try
        {
            string catalog = Path.Combine(root, "catalog");
            string one = F09Checks.WriteScene(catalog, "one", "scene-one");
            _ = F09Checks.WriteScene(catalog, "two", "scene-two");
            string path = Path.Combine(root, "state.json");
            var repository = new StudioStateRepository(path);
            var initial = StudioState.Empty with
            {
                Applications = [new("one", "One", "legacy"), new("two", "Two", "legacy")],
                Settings = new("dark", true)
            };
            if (repository.Save(initial) != RepositoryError.None) throw new IOException("Fixture save failed");
            var store = new ProbeStore(repository);
            bool eligibility = true, throwEligibility = false, insideTransaction = true;
            int probes = 0;
            var service = new BindingService(store, () => new BuiltInSceneCatalog().Scan(catalog),
                _ => new ExecutableTarget(Environment.ProcessPath!, []), sceneEligibility: item =>
                {
                    probes++; insideTransaction &= store.InTransaction;
                    if (throwEligibility) throw new IOException("Injected unavailable reader");
                    var bytes = SceneAssetResolver.ReadFile(item.DirectoryPath, "scene.playback.json", ScenePlaybackReader.MaximumBytes);
                    return eligibility && bytes.Error == SceneAssetError.None && ScenePlaybackReader.Read(bytes.Bytes!, item.Manifest!).Error == SceneDataError.None;
                });
            BindingMutationResult first = service.Bind("one", "scene-one");
            BindingMutationResult second = service.Bind("two", "scene-one");
            Check("current playable scene binds to two apps inside the transaction", first.Status == BindingMutationStatus.Applied && second.Status == BindingMutationStatus.Applied && insideTransaction && probes == 2);
            byte[] before = File.ReadAllBytes(path), backup = File.ReadAllBytes(path + ".bak");
            eligibility = false;
            Check("unavailable existing identical binding is a byte-preserving no-op", service.Bind("one", "scene-one").Status == BindingMutationStatus.Unchanged && File.ReadAllBytes(path).SequenceEqual(before) && File.ReadAllBytes(path + ".bak").SequenceEqual(backup));
            Check("resolve reports current playback unavailability without deleting binding", service.Resolve("one") is { Status: BindingResolutionStatus.SceneUnavailable, Binding.BindingId: var preserved } && preserved == first.Binding!.BindingId && File.ReadAllBytes(path).SequenceEqual(before));
            Check("replacement rejects ineligible work and preserves bytes", service.Bind("one", "scene-two").Status == BindingMutationStatus.SceneUnavailable && File.ReadAllBytes(path).SequenceEqual(before));
            Check("disable remains possible for unavailable work", service.SetEnabled("one", false).Status == BindingMutationStatus.Applied);
            before = File.ReadAllBytes(path); backup = File.ReadAllBytes(path + ".bak");
            Check("enable checks current playability and preserves prior bytes", service.SetEnabled("one", true).Status == BindingMutationStatus.SceneUnavailable && File.ReadAllBytes(path).SequenceEqual(before) && File.ReadAllBytes(path + ".bak").SequenceEqual(backup));
            eligibility = true; throwEligibility = true;
            Check("eligibility exception fails closed for scene only", service.Bind("one", "scene-two").Status == BindingMutationStatus.SceneUnavailable && File.ReadAllBytes(path).SequenceEqual(before));
            throwEligibility = false;
            BindingMutationResult replaced = service.Bind("one", "scene-two");
            Check("replacement keeps binding ID unrelated app and settings", replaced.Binding?.BindingId == first.Binding!.BindingId && repository.Load().State!.Bindings.Single(item => item.AppId == "two") == second.Binding && repository.Load().State!.Settings == initial.Settings);
            before = File.ReadAllBytes(path); backup = File.ReadAllBytes(path + ".bak");
            store.BeforeUpdate = () => File.Delete(Path.Combine(one, "scene.playback.json"));
            Check("playback removed after selection is rejected at commit time", service.Bind("one", "scene-one").Status == BindingMutationStatus.SceneUnavailable && File.ReadAllBytes(path).SequenceEqual(before) && File.ReadAllBytes(path + ".bak").SequenceEqual(backup));
            store.BeforeUpdate = null;
            store.Error = RepositoryError.Busy;
            Check("Busy remains structured and preserves bytes", service.Bind("one", "scene-two") is { Status: BindingMutationStatus.StorageError, RepositoryError: RepositoryError.Busy } && File.ReadAllBytes(path).SequenceEqual(before));
            store.Error = RepositoryError.IoError;
            Check("I/O error remains structured and preserves bytes", service.Bind("one", "scene-two") is { Status: BindingMutationStatus.StorageError, RepositoryError: RepositoryError.IoError } && File.ReadAllBytes(path).SequenceEqual(before));
            store.Error = RepositoryError.None;
            Check("removed app is reported without mutation", service.Bind("removed", "scene-one").Status == BindingMutationStatus.ApplicationMissing && File.ReadAllBytes(path).SequenceEqual(before));
            var legacy = new BindingService(store, () => new BuiltInSceneCatalog().Scan(catalog), _ => new ExecutableTarget(Environment.ProcessPath!, []));
            Check("default policy retains F07 manifest-only compatibility", legacy.Bind("one", "scene-one").Status == BindingMutationStatus.Applied);
        }
        finally { Directory.Delete(root, true); }
        return failures;
    }

    private sealed class ProbeStore(IStudioStateStore inner) : IStudioStateStore
    {
        internal bool InTransaction;
        internal Action? BeforeUpdate;
        internal RepositoryError Error;
        public RepositoryResult Load() => inner.Load();
        public RepositoryResult Update(Func<StudioState, StudioState> transition)
        {
            if (Error != RepositoryError.None) return new(null, Error);
            return inner.Update(state => { BeforeUpdate?.Invoke(); InTransaction = true; try { return transition(state); } finally { InTransaction = false; } });
        }
    }
}
