using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace DesktopMotionStudio.Core.Diagnostics;

public enum DiagnosticAdapter { Unknown, Executable, Packaged }
public enum DiagnosticStage { Request, Target, Animation }
public enum DiagnosticResultCode
{
    InvalidRequest, ConfigurationUnavailable, BindingMissing, TargetUnavailable, RecursiveTarget,
    Existing, WindowReady, ForegroundDenied, NoUsableWindow, AmbiguousWindow, TimedOut, ProcessExited,
    IdentityChanged, IdentityUnavailable, LaunchFailed, Busy, Canceled, HandedOff, Skipped, Exited,
    RendererFailed, NotStarted, Completed, UnexpectedFailure
}
public enum DiagnosticFallback { None, AnimationDisabled, PlaybackUnavailable, RendererUnavailable, RendererFailed }

public readonly record struct DiagnosticVersion(
    [property: JsonRequired] ushort Major,
    [property: JsonRequired] ushort Minor,
    [property: JsonRequired] ushort Build,
    [property: JsonRequired] ushort Revision)
{
    public override string ToString() => FormattableString.Invariant($"{Major}.{Minor}.{Build}.{Revision}");
}

public sealed record DiagnosticEvent(
    [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] DateTimeOffset TimestampUtc,
    [property: JsonRequired] DiagnosticVersion ProductVersion,
    [property: JsonRequired] int WindowsBuild,
    [property: JsonRequired] DiagnosticAdapter Adapter,
    [property: JsonRequired] DiagnosticStage Stage,
    [property: JsonRequired] DiagnosticResultCode Result,
    [property: JsonRequired] DiagnosticFallback Fallback,
    [property: JsonRequired] int ElapsedMilliseconds)
{
    internal bool IsValid => RequestId != Guid.Empty && TimestampUtc.Offset == TimeSpan.Zero &&
        WindowsBuild > 0 && ElapsedMilliseconds is >= 0 and <= 120_000 &&
        Enum.IsDefined(Adapter) && Enum.IsDefined(Stage) && Enum.IsDefined(Result) && Enum.IsDefined(Fallback);
}

public static class DiagnosticReport
{
    public static string Format(IReadOnlyList<DiagnosticEvent> events)
    {
        var report = new StringBuilder("Desktop Motion Studio · Local diagnostics");
        foreach (DiagnosticEvent item in events.TakeLast(12).Reverse())
        {
            if (!item.IsValid) continue;
            report.AppendLine().Append(CultureInfo.InvariantCulture,
                $"{item.TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC | {item.RequestId:N} | {item.ProductVersion} | Windows {item.WindowsBuild} | {item.Adapter} | {item.Stage} | {item.Result} | {item.Fallback} | {item.ElapsedMilliseconds} ms");
        }
        return report.ToString();
    }
}
