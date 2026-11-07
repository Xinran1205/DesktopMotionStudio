using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.Shortcuts;

namespace DesktopMotionStudio.Studio;

internal enum TestLaunchClientStatus { Completed, ComponentUnavailable, TimedOut, Detached }

internal sealed record TestLaunchResult(TestLaunchClientStatus Status, TestLaunchOutcome Outcome = TestLaunchOutcome.Failed);

internal sealed class TestLaunchClient(string launcher,
    Func<ProcessStartInfo, CancellationToken, Task<int>>? run = null, TimeSpan? timeout = null)
{
    internal async Task<TestLaunchResult> LaunchAsync(string bindingId, CancellationToken stopWaiting)
    {
        if (stopWaiting.IsCancellationRequested) return new(TestLaunchClientStatus.Detached);
        string? token = ManagedLaunchRequest.EncodeBindingId(bindingId);
        if (token is null) return new(TestLaunchClientStatus.Completed, TestLaunchOutcome.BindingUnavailable);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopWaiting);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(60));
        try
        {
            if (!LaunchTargetIdentityResolver.IsLocalPath(launcher) || !File.Exists(launcher))
                return new(TestLaunchClientStatus.ComponentUnavailable);
            var start = new ProcessStartInfo(launcher)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(launcher)!
            };
            start.ArgumentList.Add(TestLaunchProtocol.Command);
            start.ArgumentList.Add(token);
            int code = await (run ?? RunAsync)(start, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
            return new(TestLaunchClientStatus.Completed,
                Enum.IsDefined((TestLaunchOutcome)code) ? (TestLaunchOutcome)code : TestLaunchOutcome.Failed);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return new(stopWaiting.IsCancellationRequested ? TestLaunchClientStatus.Detached : TestLaunchClientStatus.TimedOut);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            return new(TestLaunchClientStatus.ComponentUnavailable);
        }
    }

    private static Task<int> RunAsync(ProcessStartInfo start, CancellationToken token) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();
        using Process process = Process.Start(start) ?? throw new IOException("Launcher did not start.");
        // This wait is owned by Studio. Disposing the handle does not terminate the request.
        await process.WaitForExitAsync(token).ConfigureAwait(false);
        return process.ExitCode;
    }, token);
}
