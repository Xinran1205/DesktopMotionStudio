using System.IO;
using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Preview;
using DesktopMotionStudio.Studio;

internal static class PreviewIntentChecks
{
    internal static void Run(ref int failures) => failures += RunAsync().GetAwaiter().GetResult();
    private static async Task<int> RunAsync()
    {
        int failures = 0;
        string root = Path.Combine(Path.GetTempPath(), $"dms-f09-intent-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        try
        {
            F09WindowChecks.WriteScene(root, "scene-one", "One");
            var item = new SceneWorkspace(root).FindPlayable("scene-one")!;
            foreach (string operation in new[] { "stop", "replace", "close" })
            {
                using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
                var factory = new Factory(); await using var coordinator = new PreviewCoordinator(factory);
                int reads = 0;
                var intents = new PreviewIntentController(coordinator, _ => { if (Interlocked.Increment(ref reads) == 1) { entered.Set(); release.Wait(TimeSpan.FromSeconds(3)); } return item; });
                Task<PreviewResult> old = intents.PlayAsync("scene-one");
                if (!await Task.Run(() => entered.Wait(2000))) throw new TimeoutException("Preflight fixture did not enter");
                PreviewResult? latest = null;
                if (operation == "stop") await intents.StopAsync();
                else if (operation == "close") await intents.DisposeAsync();
                else latest = await intents.PlayAsync("scene-one");
                release.Set(); PreviewResult stale = await old;
                bool passed = operation == "replace"
                    ? factory.Starts == 1 && stale.Outcome == PreviewOutcome.Superseded && latest!.Outcome == PreviewOutcome.Completed && intents.IsCurrent(latest)
                    : factory.Starts == 0 && stale.Outcome == (operation == "close" ? PreviewOutcome.OwnerClosed : PreviewOutcome.Superseded);
                Console.WriteLine($"{(passed ? "PASS" : "FAIL")} preview UI preflight {operation} rejects an older delayed scan");
                if (!passed) failures++;
                await intents.DisposeAsync();
            }
        }
        finally { Directory.Delete(root, true); }
        return failures;
    }
    private sealed class Factory : IPreviewSessionFactory
    {
        internal int Starts;
        public IPreviewSession Start(PreviewRequest request) { Interlocked.Increment(ref Starts); return new Session(); }
    }
    private sealed class Session : IPreviewSession
    {
        public Task<MotionHostOutcome?> Started => Task.FromResult<MotionHostOutcome?>(null);
        public Task<MotionHostOutcome> Completion => Task.FromResult(MotionHostOutcome.Completed);
        public MotionHostError? FailureReason => null;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
