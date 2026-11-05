using DesktopMotionStudio.Core.Instances;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.Scenes;

namespace DesktopMotionStudio.Core.MotionHost;

public sealed class MotionHostLifecycleCoordinator
{
    public static readonly TimeSpan HostCleanupTimeout = TimeSpan.FromSeconds(1);
    private readonly ApplicationInstanceCoordinator _instances;
    private readonly IMotionHostSessionFactory _hostFactory;

    public MotionHostLifecycleCoordinator(
        ApplicationInstanceCoordinator instances,
        IMotionHostSessionFactory hostFactory)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _hostFactory = hostFactory ?? throw new ArgumentNullException(nameof(hostFactory));
    }

    public async Task<MotionHostLifecycleResult> OpenAsync(
        string appId,
        LaunchTarget target,
        SceneManifest? scene,
        CancellationToken cancellationToken = default,
        ScenePlaybackDefinition? playback = null)
    {
        if (scene is null || !IsValidScene(scene) || playback is not null && (!playback.IsValid || playback.SceneId != scene.SceneId || playback.SceneVersion != scene.SceneVersion))
        {
            InstanceResult invalidSceneTarget = await _instances.OpenAsync(appId, target, cancellationToken).ConfigureAwait(false);
            return new MotionHostLifecycleResult(invalidSceneTarget, MotionHostOutcome.Failed, MotionHostError.InvalidMessage);
        }

        var startRequest = new MotionHostStartRequest(
            Guid.NewGuid(), scene.SceneId, scene.SceneVersion, scene.DisplayMode, Playback: playback);
        IMotionHostSession? session = null;
        bool beforeDispatchCalled = false;
        MotionHostError? hostError = null;
        int? nativeError = null;

        void StartHost()
        {
            beforeDispatchCalled = true;
            try
            {
                session = _hostFactory.Start(startRequest);
            }
            catch (Exception ex)
            {
                hostError = MotionHostError.StartFailed;
                nativeError = ex.HResult;
                throw;
            }
        }

        InstanceResult targetResult;
        try
        {
            targetResult = await _instances.OpenAsync(
                appId, target, cancellationToken, StartHost).ConfigureAwait(false);
        }
        catch
        {
            if (session is not null)
            {
                try { await session.ShutdownAsync().WaitAsync(HostCleanupTimeout).ConfigureAwait(false); }
                catch (Exception) { }
                try { await session.DisposeAsync().AsTask().WaitAsync(HostCleanupTimeout).ConfigureAwait(false); }
                catch (Exception) { }
            }
            throw;
        }

        if (!beforeDispatchCalled)
            return new MotionHostLifecycleResult(targetResult, MotionHostOutcome.NotStarted,
                targetResult.LaunchPreparationErrorCode is null ? null : MotionHostError.StartFailed,
                targetResult.LaunchPreparationErrorCode);

        if (session is null)
            return new MotionHostLifecycleResult(targetResult, MotionHostOutcome.Failed,
                hostError ?? MotionHostError.StartFailed,
                nativeError ?? targetResult.LaunchPreparationErrorCode);

        MotionHostOutcome hostOutcome;
        try
        {
            hostOutcome = targetResult.Outcome == InstanceOutcome.WindowReady
                ? await RunBoundedAsync(session.HandoffAsync, HostCleanupTimeout).ConfigureAwait(false)
                : await RunBoundedAsync(session.ShutdownAsync, HostCleanupTimeout).ConfigureAwait(false);
            if (hostOutcome is MotionHostOutcome.Failed or MotionHostOutcome.TimedOut)
                hostError ??= hostOutcome == MotionHostOutcome.TimedOut
                    ? MotionHostError.TimedOut
                    : session.FailureReason ?? MotionHostError.RendererFailed;
            nativeError ??= session.NativeErrorCode;
        }
        catch (TimeoutException)
        {
            hostOutcome = MotionHostOutcome.Failed;
            hostError = MotionHostError.TimedOut;
        }
        catch (Exception ex)
        {
            hostOutcome = MotionHostOutcome.Failed;
            hostError = MotionHostError.RendererFailed;
            nativeError = ex.HResult;
        }
        finally
        {
            try { await session.DisposeAsync().AsTask().WaitAsync(HostCleanupTimeout).ConfigureAwait(false); }
            catch (Exception ex)
            {
                hostError ??= MotionHostError.RendererFailed;
                nativeError ??= ex.HResult;
            }
        }

        return new MotionHostLifecycleResult(targetResult, hostOutcome, hostError, nativeError);
    }

    private static async Task<MotionHostOutcome> RunBoundedAsync(
        Func<CancellationToken, Task<MotionHostOutcome>> operation,
        TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            Task<MotionHostOutcome> task = operation(timeoutSource.Token);
            return await task.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException("MotionHost cleanup operation exceeded its deadline.");
        }
    }

    private static bool IsValidScene(SceneManifest scene) =>
        scene.SchemaVersion == SceneManifestReader.SupportedSchemaVersion &&
        SceneManifestReader.IsValidSceneId(scene.SceneId) &&
        SceneManifestReader.IsValidSceneVersion(scene.SceneVersion) &&
        !string.IsNullOrWhiteSpace(scene.Name) && scene.Name.Length <= 120 &&
        scene.DisplayMode is SceneDisplayMode.Full or SceneDisplayMode.Centered;
}
