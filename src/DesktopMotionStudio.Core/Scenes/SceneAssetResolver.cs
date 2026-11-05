using DesktopMotionStudio.Core.IO;

namespace DesktopMotionStudio.Core.Scenes;

public enum SceneAssetError { None, InvalidPath, Missing, ReparsePoint, IoError, TooLarge, NotAFile }
public sealed record SceneAssetResolution(string? FullPath, SceneAssetError Error);
public sealed record SceneAssetReadResult(byte[]? Bytes, SceneAssetError Error);

public static class SceneAssetResolver
{
    public static bool IsSafeRelativePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.Length <= 2048 && !path.Contains('\\') &&
        path.Split('/').All(LocalPathPolicy.IsUnambiguousName);

    public static SceneAssetError ValidateDirectory(string path)
    {
        LocalPathError error = LocalPathPolicy.Inspect(path, out _, out FileAttributes attributes);
        if (error != LocalPathError.None) return Map(error);
        return (attributes & FileAttributes.Directory) != 0 ? SceneAssetError.None : SceneAssetError.InvalidPath;
    }

    public static SceneAssetResolution Resolve(string sceneDirectory, string relativePath)
    {
        if (!IsSafeRelativePath(relativePath)) return new(null, SceneAssetError.InvalidPath);
        SceneAssetError directoryError = ValidateDirectory(sceneDirectory);
        if (directoryError != SceneAssetError.None) return new(null, directoryError);
        try
        {
            string root = Path.GetFullPath(sceneDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!target.StartsWith(root, comparison)) return new(null, SceneAssetError.InvalidPath);
            LocalPathError error = LocalPathPolicy.Inspect(target, out string fullPath, out FileAttributes attributes);
            if (error != LocalPathError.None) return new(null, Map(error));
            return (attributes & FileAttributes.Directory) == 0
                ? new(fullPath, SceneAssetError.None) : new(null, SceneAssetError.NotAFile);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return new(null, SceneAssetError.InvalidPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(null, SceneAssetError.IoError); }
    }

    public static SceneAssetReadResult ReadFile(string sceneDirectory, string relativePath, int maximumBytes)
    {
        if (maximumBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        SceneAssetResolution resolution = Resolve(sceneDirectory, relativePath);
        if (resolution.Error != SceneAssetError.None) return new(null, resolution.Error);
        try
        {
            using var stream = new FileStream(resolution.FullPath!, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length > maximumBytes) return new(null, SceneAssetError.TooLarge);
            byte[] bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            return stream.ReadByte() == -1 ? new(bytes, SceneAssetError.None) : new(null, SceneAssetError.TooLarge);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return new(null, SceneAssetError.Missing); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(null, SceneAssetError.IoError); }
    }

    public static SceneAssetError ProbeFile(string sceneDirectory, string relativePath, int maximumBytes)
    {
        if (maximumBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        SceneAssetResolution resolution = Resolve(sceneDirectory, relativePath);
        if (resolution.Error != SceneAssetError.None) return resolution.Error;
        try { return new FileInfo(resolution.FullPath!).Length <= maximumBytes ? SceneAssetError.None : SceneAssetError.TooLarge; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return SceneAssetError.Missing; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return SceneAssetError.IoError; }
    }

    private static SceneAssetError Map(LocalPathError error) => error switch
    {
        LocalPathError.None => SceneAssetError.None,
        LocalPathError.Missing => SceneAssetError.Missing,
        LocalPathError.ReparsePoint => SceneAssetError.ReparsePoint,
        LocalPathError.IoError => SceneAssetError.IoError,
        _ => SceneAssetError.InvalidPath
    };
}
