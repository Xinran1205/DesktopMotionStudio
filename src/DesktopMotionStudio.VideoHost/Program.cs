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
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ||
            args.Length is not (4 or 6) || args[0] != "--pipe" || args[2] != "--request-id" ||
            !Guid.TryParseExact(args[3], "D", out Guid id) || id == Guid.Empty ||
            args[1] != $"dms-motionhost-{id:N}" ||
            args.Length == 6 && (args[4] != "--language" || args[5] != ProductText.English)) return 2;

        ProductText.UseLanguage(args.Length == 6 ? ProductText.English : ProductText.DefaultLanguage);
        using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            using var startup = new CancellationTokenSource(VideoPlaybackRequest.StartupMilliseconds);
            pipe.ConnectAsync(startup.Token).GetAwaiter().GetResult();
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

    private static VideoEventKind Render(VideoPlaybackRequest request, Action visible)
    {
        string path = request.Clip.AssetPath;
        if (!LaunchTargetIdentityResolver.IsLocalPath(path)) return VideoEventKind.InvalidMedia;
        using var asset = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (asset.Length is 0 or > VideoClip.MaximumBytes || !VideoMediaProfile.HasMp4Header(asset)) return VideoEventKind.InvalidMedia;
        bool reducedMotion = !VideoStage.SystemAnimationsEnabled();
        using var video = new NativeVideo();
        video.Open(path, request.Clip.Sound && !reducedMotion);
        using var stage = new VideoStage(video, request, visible, reducedMotion);
        return stage.Run();
    }
}
