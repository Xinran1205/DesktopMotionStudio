using System.Buffers.Text;
using System.Diagnostics;
using System.Text;
using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.State;
using DesktopMotionStudio.Core.Video;

namespace DesktopMotionStudio.Core.Shortcuts;

public enum ManagedLaunchStatus { Ready, InvalidBindingId, StorageError, BindingMissing, TargetUnavailable, RecursiveTarget }
public enum ManagedPlaybackStatus { Unavailable, Disabled, Ready }

public sealed record ManagedLaunchPlan(
    ManagedLaunchStatus Status,
    string? AppId = null,
    LaunchTarget? Target = null,
    SceneManifest? Scene = null,
    ScenePlaybackDefinition? Playback = null,
    RepositoryError StorageError = RepositoryError.None,
    string? DisplayName = null,
    ManagedPlaybackStatus PlaybackStatus = ManagedPlaybackStatus.Unavailable)
{
    public VideoClip? Video { get; init; }
}

public sealed class ManagedLaunchRequest(IStudioStateStore store, string catalogRoot)
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly string[] ComponentNames =
    [
        "DesktopMotionStudio.Launcher.exe",
        "DesktopMotionStudio.Studio.exe",
        "DesktopMotionStudio.MotionHost.exe",
        VideoPlaybackRequest.WorkerName
    ];

    public static string? EncodeBindingId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128) return null;
        try { return Base64Url.EncodeToString(StrictUtf8.GetBytes(id)); }
        catch (EncoderFallbackException) { return null; }
    }

    public static string? ParseArguments(string[] args)
    {
        if (args is not ["--binding", string token] || token.Length is 0 or > 512 ||
            token.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            return null;

        try
        {
            string id = StrictUtf8.GetString(Base64Url.DecodeFromChars(token));
            return EncodeBindingId(id) == token ? id : null;
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException) { return null; }
    }

    public ManagedLaunchPlan Resolve(string bindingId)
    {
        if (EncodeBindingId(bindingId) is null) return new(ManagedLaunchStatus.InvalidBindingId);

        RepositoryResult loaded = store.Load();
        if (!loaded.IsSuccess) return new(ManagedLaunchStatus.StorageError, StorageError: loaded.Error);
        return Resolve(bindingId, loaded.State!);
    }

    internal ManagedLaunchPlan Resolve(string bindingId, StudioState state)
    {
        ApplicationBinding? binding = state.Bindings.FirstOrDefault(item =>
            string.Equals(item.BindingId, bindingId, StringComparison.OrdinalIgnoreCase));
        if (binding is null) return new(ManagedLaunchStatus.BindingMissing);
        ApplicationProfile? app = state.Applications.FirstOrDefault(item =>
            string.Equals(item.AppId, binding.AppId, StringComparison.OrdinalIgnoreCase));
        if (app is null || ApplicationRegistry.GetTarget(app) is not LaunchTarget target)
            return new(ManagedLaunchStatus.TargetUnavailable);

        LaunchTargetResolution resolution = new LaunchTargetIdentityResolver().Resolve(target);
        if (!resolution.Succeeded) return new(ManagedLaunchStatus.TargetUnavailable);
        if (resolution.Target is ResolvedExecutableLaunch executable)
        {
            string path = executable.ExecutableIdentity.Path;
            if (IsProductExecutable(path)) return new(ManagedLaunchStatus.RecursiveTarget);
            // Preserve the resolved command so a later edit to the original .lnk cannot redirect this request.
            target = new ExecutableTarget(path, executable.Arguments, executable.WorkingDirectory);
        }

        var native = new ManagedLaunchPlan(ManagedLaunchStatus.Ready, app.AppId, target, DisplayName: app.DisplayName);
        if (!binding.Enabled) return native with { PlaybackStatus = ManagedPlaybackStatus.Disabled };
        SceneCatalogItem? item = new BuiltInSceneCatalog().Scan(catalogRoot).Items.FirstOrDefault(candidate =>
            candidate.IsAvailable && string.Equals(candidate.Manifest?.SceneId, binding.SceneId, StringComparison.OrdinalIgnoreCase));
        if (item?.Manifest is not SceneManifest manifest) return native;

        if (VideoSceneReader.UsesVideo(manifest))
        {
            var video = VideoSceneReader.Read(item);
            return video is { Error: SceneDataError.None, Value: { } clip }
                ? native with { Scene = manifest, Video = clip with { Sound = clip.Sound && !state.Settings.Muted }, PlaybackStatus = ManagedPlaybackStatus.Ready }
                : native;
        }

        SceneAssetReadResult read = SceneAssetResolver.ReadFile(item.DirectoryPath, "scene.playback.json", ScenePlaybackReader.MaximumBytes);
        if (read.Error != SceneAssetError.None) return native;
        SceneDataReadResult<ScenePlaybackDefinition> playback = ScenePlaybackReader.Read(read.Bytes!, manifest);
        return playback is { Error: SceneDataError.None, Value: not null }
            ? native with { Scene = manifest, Playback = playback.Value, PlaybackStatus = ManagedPlaybackStatus.Ready }
            : native;
    }

    public static bool IsProductExecutable(string path)
    {
        if (ComponentNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase) ||
            string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            // The original resource name survives renaming and copying a bundled apphost.
            string? original = FileVersionInfo.GetVersionInfo(path).OriginalFilename;
            return original is not null && ComponentNames.Any(name =>
                string.Equals(name, original, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.ChangeExtension(name, ".dll"), original, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
