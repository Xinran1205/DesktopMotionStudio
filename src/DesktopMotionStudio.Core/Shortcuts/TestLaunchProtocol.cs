namespace DesktopMotionStudio.Core.Shortcuts;

public static class TestLaunchProtocol
{
    public const string Command = "--test-binding";

    public static string? ParseArguments(string[] args) => args is [Command, string token]
        ? ManagedLaunchRequest.ParseArguments(["--binding", token]) : null;
}

public enum TestLaunchOutcome
{
    Opened = 0,
    OpenedWithoutMotion = 10,
    ExistingWindow = 11,
    BindingUnavailable = 12,
    TargetUnavailable = 13,
    ConfigurationUnavailable = 14,
    TimedOut = 15,
    Busy = 16,
    Failed = 17,
    ComponentUnavailable = 18
}
