using System.Text.RegularExpressions;
using DesktopMotionStudio.Core.MotionHost;

namespace DesktopMotionStudio.MotionHost;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!HostArguments.TryParse(args, out HostArguments? parsed) || parsed is null) return 2;
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive) return 3;

        ApplicationConfiguration.Initialize();
        HostProtocolClient? client = null;
        try
        {
            client = HostProtocolClient.ConnectAndStartAsync(parsed).GetAwaiter().GetResult();
            var window = new MotionHostWindow(client.StartPayload);
            HostProtocolClient capturedClient = client;
            window.Shown += (_, _) => _ = NotifyVisibleAndStartListenerAsync(capturedClient, window);
            Application.Run(window);
            client.ReportExitAsync(window.ExitReason).GetAwaiter().GetResult();
            return window.ExitReason == MotionHostExitReason.Failed ? 4 : 0;
        }
        catch (Exception)
        {
            if (client is not null)
            {
                try { client.ReportFailureAsync(MotionHostError.HostInitializationFailed).GetAwaiter().GetResult(); }
                catch (Exception) { }
            }
            return 4;
        }
        finally
        {
            if (client is not null) client.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static async Task NotifyVisibleAndStartListenerAsync(HostProtocolClient client, MotionHostWindow window)
    {
        try
        {
            await client.SendStartedAsync().ConfigureAwait(false);
            client.StartListener(window);
        }
        catch (Exception)
        {
            window.RequestImmediateExit(MotionHostExitReason.Failed);
        }
    }
}

internal sealed record HostArguments(string PipeName, Guid RequestId)
{
    private static readonly Regex PipeNamePattern = new(
        "^dms-motionhost-[0-9a-f]{32}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    public static bool TryParse(string[] args, out HostArguments? parsed)
    {
        parsed = null;
        if (args.Length != 4 || args[0] != "--pipe" || args[2] != "--request-id") return false;
        if (args[1].Length > 64 || !PipeNamePattern.IsMatch(args[1]) ||
            !Guid.TryParseExact(args[3], "D", out Guid requestId) || requestId == Guid.Empty)
            return false;

        string expectedName = $"dms-motionhost-{requestId:N}";
        if (!string.Equals(args[1], expectedName, StringComparison.OrdinalIgnoreCase)) return false;
        parsed = new HostArguments(args[1], requestId);
        return true;
    }
}
