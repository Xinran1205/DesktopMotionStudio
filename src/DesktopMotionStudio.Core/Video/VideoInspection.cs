using System.ComponentModel;
using System.IO.Pipes;
using System.Text.Json.Serialization;
using DesktopMotionStudio.Core.MotionHost;

namespace DesktopMotionStudio.Core.Video;

public enum VideoInspectionError { None, InvalidMedia, Unavailable, TimedOut }

public sealed record VideoInspectionResult(VideoInspectionError Error, long DurationMilliseconds = 0, bool HasAudio = false)
{
    internal bool IsValid => Enum.IsDefined(Error) && (Error == VideoInspectionError.None
        ? DurationMilliseconds >= 1000
        : DurationMilliseconds == 0 && !HasAudio);
}

public sealed record VideoInspectionRequest(
    [property: JsonRequired] int Version,
    [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] string AssetPath)
{
    public const int CurrentVersion = 1;
    public const int TimeoutMilliseconds = 5000;
    [JsonIgnore]
    public bool IsValid => Version == CurrentVersion && RequestId != Guid.Empty &&
        AssetPath is { Length: > 0 and <= 1024 } && Path.IsPathFullyQualified(AssetPath) &&
        !AssetPath.Any(char.IsControl) && string.Equals(Path.GetExtension(AssetPath), ".mp4", StringComparison.OrdinalIgnoreCase);
}

public sealed record VideoInspectionReply(
    [property: JsonRequired] int Version,
    [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] VideoInspectionResult Result)
{
    internal bool Matches(Guid id) => Version == VideoInspectionRequest.CurrentVersion && RequestId == id &&
        Result is { IsValid: true };
}

public sealed class VideoInspector(string workerPath)
{
    public async Task<VideoInspectionResult> InspectAsync(string assetPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = new VideoInspectionRequest(VideoInspectionRequest.CurrentVersion, Guid.NewGuid(), assetPath);
        if (!request.IsValid) return new(VideoInspectionError.InvalidMedia);
        if (!File.Exists(workerPath)) return new(VideoInspectionError.Unavailable);

        string pipeName = $"dms-motionhost-{request.RequestId:N}";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(VideoInspectionRequest.TimeoutMilliseconds);
        OwnedMotionHostProcess? worker = null;
        VideoInspectionResult result;
        try
        {
            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            worker = OwnedMotionHostProcess.Start(workerPath, pipeName, request.RequestId, inspectVideo: true);
            await pipe.WaitForConnectionAsync(deadline.Token).ConfigureAwait(false);
            await VideoPlaybackWire.WriteAsync(pipe, request, deadline.Token).ConfigureAwait(false);
            var reply = await VideoPlaybackWire.ReadAsync<VideoInspectionReply>(pipe, deadline.Token).ConfigureAwait(false);
            result = reply.Matches(request.RequestId) ? reply.Result : new(VideoInspectionError.Unavailable);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { result = new(VideoInspectionError.TimedOut); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        { result = new(VideoInspectionError.Unavailable); }
        finally
        {
            if (worker is not null)
            {
                using (worker)
                {
                    worker.Terminate();
                    try
                    {
                        await worker.Process.WaitForExitAsync().WaitAsync(
                            TimeSpan.FromMilliseconds(VideoPlaybackRequest.CleanupMilliseconds)).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is TimeoutException or Win32Exception or InvalidOperationException)
                    { result = new(VideoInspectionError.Unavailable); }
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}
