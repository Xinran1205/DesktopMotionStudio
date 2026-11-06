using System.Text.Json;
using DesktopMotionStudio.Core.State;

namespace DesktopMotionStudio.Launcher;

// Uses the production entry implementation with an explicit isolated fixture store.
internal static class FixtureProgram
{
    [STAThread]
    private static int Main(string[] args)
    {
        string? profile = Environment.GetEnvironmentVariable("DMS_F10_FIXTURE_STATE");
        if (profile is null || !Path.IsPathFullyQualified(profile) || !File.Exists(profile)) return 2;
        return Program.Run(args, () => new StudioStateRepository(profile), (_, code) => code,
            (plan, instance, host) => File.WriteAllText(profile + ".launch-trace", JsonSerializer.Serialize(new
            {
                Scene = plan.Scene?.SceneId,
                Instance = instance.Outcome.ToString(),
                TargetPid = instance.Process?.ProcessId,
                Window = (long)(instance.WindowHandle ?? 0),
                Host = host?.HostOutcome.ToString(),
                HostError = host?.HostError?.ToString()
            })));
    }
}
