using DesktopMotionStudio.Core.Preview;
using DesktopMotionStudio.Core.Scenes;

namespace DesktopMotionStudio.Studio;

// Orders UI intent before asynchronous catalog validation, then submits atomically with Stop/close.
internal sealed class PreviewIntentController(PreviewCoordinator coordinator, Func<string, SceneLibraryItem?> find) : IAsyncDisposable
{
    private readonly object _gate = new();
    private long _version;
    private Guid? _request;
    private bool _closed;

    internal bool IsCurrent(PreviewResult result) { lock (_gate) return !_closed && _request == result.RequestId; }
    internal void CancelPending() { lock (_gate) { _version++; _request = null; } }

    internal async Task<PreviewResult> PlayAsync(string sceneId)
    {
        long version; Guid request = Guid.NewGuid();
        lock (_gate)
        {
            if (_closed) return new(request, sceneId, PreviewOutcome.OwnerClosed);
            version = ++_version; _request = request;
        }
        SceneLibraryItem? item;
        try { item = await Task.Run(() => find(sceneId)).ConfigureAwait(false); }
        catch (Exception) { item = null; }
        Task<PreviewResult> playback;
        lock (_gate)
        {
            if (_closed) return new(request, sceneId, PreviewOutcome.OwnerClosed);
            if (version != _version) return new(request, sceneId, PreviewOutcome.Superseded);
            if (item?.IsPlayable != true) return new(request, sceneId, PreviewOutcome.Unavailable);
            playback = coordinator.PlayAsync(new(request, item.Playback!, item.Catalog.Manifest!.DisplayMode));
        }
        return await playback.ConfigureAwait(false);
    }

    internal Task StopAsync()
    {
        lock (_gate) { _version++; _request = null; return coordinator.StopAsync(); }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) { _closed = true; _version++; _request = null; return coordinator.DisposeAsync(); }
    }
}
