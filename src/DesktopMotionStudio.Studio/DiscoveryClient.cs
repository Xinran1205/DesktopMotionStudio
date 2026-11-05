using System.Diagnostics;
using System.IO;
using DesktopMotionStudio.Core.Applications;

namespace DesktopMotionStudio.Studio;

internal sealed class DiscoveryClient
{
    private readonly Func<ProcessStartInfo> _createStart;
    private readonly TimeSpan _timeout;

    internal DiscoveryClient(Func<ProcessStartInfo>? createStart = null, TimeSpan? timeout = null)
    {
        _createStart = createStart ?? CreateStart;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromSeconds(5)) throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    internal async Task<ApplicationDiscoveryResult> ScanAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return new(ApplicationDiscoveryStatus.Cancelled, []);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        using var process = new Process();
        try
        {
            ProcessStartInfo start = _createStart();
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            process.StartInfo = start;
            if (!process.Start()) return new(ApplicationDiscoveryStatus.Failed, []);
            Task<byte[]> output = ReadBoundedAsync(process.StandardOutput.BaseStream, ApplicationDiscoveryProtocol.MaximumBytes, deadline.Token);
            Task<byte[]> error = ReadBoundedAsync(process.StandardError.BaseStream, 16 * 1024, deadline.Token);
            await Task.WhenAll(output, error, process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
            return process.ExitCode == 0 ? ApplicationDiscoveryProtocol.Decode(await output.ConfigureAwait(false)) : new(ApplicationDiscoveryStatus.Failed, []);
        }
        catch (OperationCanceledException)
        {
            return new(cancellationToken.IsCancellationRequested ? ApplicationDiscoveryStatus.Cancelled : ApplicationDiscoveryStatus.TimedOut, []);
        }
        catch (Exception) { return new(ApplicationDiscoveryStatus.Failed, []); }
        finally
        {
            deadline.Cancel();
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: false);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch (Exception) { }
        }
    }

    private static ProcessStartInfo CreateStart()
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Process path unavailable.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--discovery-worker");
        return start;
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, CancellationToken token)
    {
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[8192];
        while (true)
        {
            int count = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (count == 0) return bytes.ToArray();
            if (bytes.Length + count > maximum) throw new InvalidDataException("Discovery output limit.");
            bytes.Write(buffer, 0, count);
        }
    }
}
