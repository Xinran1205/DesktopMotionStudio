using System.IO.Pipes;
using System.Runtime.InteropServices;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.Localization;
using DesktopMotionStudio.Core.Video;

namespace DesktopMotionStudio.VideoHost;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool inspect = args is [_, _, _, _, "--inspect-video"];
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ||
            (!inspect && args.Length is not (4 or 6)) || args[0] != "--pipe" || args[2] != "--request-id" ||
            !Guid.TryParseExact(args[3], "D", out Guid id) || id == Guid.Empty ||
            args[1] != $"dms-motionhost-{id:N}" ||
            args.Length == 6 && (args[4] != "--language" || args[5] != ProductText.English)) return 2;

        ProductText.UseLanguage(args.Length == 6 ? ProductText.English : ProductText.DefaultLanguage);
        using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            using var startup = new CancellationTokenSource(inspect
                ? VideoInspectionRequest.TimeoutMilliseconds : VideoPlaybackRequest.StartupMilliseconds);
            pipe.ConnectAsync(startup.Token).GetAwaiter().GetResult();
            if (inspect) return Inspect(pipe, id, startup.Token);
            var request = VideoPlaybackWire.ReadAsync<VideoPlaybackRequest>(pipe, startup.Token).GetAwaiter().GetResult();
            if (!request.IsValid || request.RequestId != id) return 2;

            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            VideoEventKind outcome;
            try { outcome = Render(request, () => Notify(VideoEventKind.Visible)); }
            catch (Exception error) when (error is COMException or InvalidDataException or IOException)
            { outcome = VideoEventKind.InvalidMedia; }
            catch (Exception) { outcome = VideoEventKind.Failed; }
            Notify(outcome);
            return outcome is VideoEventKind.Completed or VideoEventKind.Skipped ? 0 : 3;
        }
        catch (Exception) { return 3; }

        void Notify(VideoEventKind kind)
        {
            using var deadline = new CancellationTokenSource(250);
            VideoPlaybackWire.WriteAsync(pipe, new VideoPlaybackEvent(VideoPlaybackRequest.CurrentVersion, id, kind),
                deadline.Token).GetAwaiter().GetResult();
        }
    }

    private static int Inspect(Stream pipe, Guid id, CancellationToken cancellationToken)
    {
        var request = VideoPlaybackWire.ReadAsync<VideoInspectionRequest>(pipe, cancellationToken).GetAwaiter().GetResult();
        if (!request.IsValid || request.RequestId != id) return 2;

        VideoInspectionResult result;
        try { result = InspectAsset(request.AssetPath, cancellationToken); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException ||
            error is COMException { HResult: unchecked((int)0x80040154) })
        { result = new(VideoInspectionError.Unavailable); }
        catch (Exception error) when (error is COMException or InvalidDataException or IOException or UnauthorizedAccessException)
        { result = new(VideoInspectionError.InvalidMedia); }

        using var replyDeadline = new CancellationTokenSource(250);
        var reply = new VideoInspectionReply(VideoInspectionRequest.CurrentVersion, id, result);
        VideoPlaybackWire.WriteAsync(pipe, reply, replyDeadline.Token).GetAwaiter().GetResult();
        return result.Error == VideoInspectionError.None ? 0 : 3;
    }

    private static VideoInspectionResult InspectAsset(string path, CancellationToken cancellationToken)
    {
        using var asset = OpenAsset(path);
        if (asset is null) return new(VideoInspectionError.InvalidMedia);
        using var video = new NativeVideo();
        video.Open(path, sound: false, inspect: true);
        double duration = video.Duration * 1000;
        if (!double.IsFinite(duration) || duration < 1000 || duration >= long.MaxValue)
            return new(VideoInspectionError.InvalidMedia);
        while (!video.TransferFrame())
            cancellationToken.ThrowIfCancellationRequested();
        return new(VideoInspectionError.None, (long)duration, video.HasAudio);
    }

    private static VideoEventKind Render(VideoPlaybackRequest request, Action visible)
    {
        string path = request.Clip.AssetPath;
        using var asset = OpenAsset(path);
        if (asset is null) return VideoEventKind.InvalidMedia;
        bool reducedMotion = !VideoStage.SystemAnimationsEnabled();
        using var video = new NativeVideo();
        video.Open(path, request.Clip.Sound && !reducedMotion);
        using var stage = new VideoStage(video, request, visible, reducedMotion);
        return stage.Run();
    }

    private static FileStream? OpenAsset(string path)
    {
        if (!LaunchTargetIdentityResolver.IsLocalPath(path)) return null;
        var asset = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (asset.Length is > 0 and <= VideoClip.MaximumBytes && VideoMediaProfile.HasMp4Header(asset)) return asset;
        }
        catch
        {
            asset.Dispose();
            throw;
        }
        asset.Dispose();
        return null;
    }
}
