using DesktopMotionStudio.Core.Preview;
using DesktopMotionStudio.Core.State;
using DesktopMotionStudio.Core.Video;

namespace DesktopMotionStudio.Studio;

internal sealed class VideoAwarePreviewFactory(IPreviewSessionFactory native, VideoPlaybackSessionFactory videos,
    IStudioStateStore store) : IPreviewSessionFactory
{
    public IPreviewSession Start(PreviewRequest request)
    {
        if (request.Video is not { } clip) return native.Start(request);
        if (!request.IsValid) throw new ArgumentException("Invalid preview request.", nameof(request));
        bool sound = clip.Sound && store.Load() is { IsSuccess: true, State.Settings.Muted: false };
        return videos.Start(request.RequestId, clip with { Sound = sound }, request.DisplayMode, preview: true);
    }
}
