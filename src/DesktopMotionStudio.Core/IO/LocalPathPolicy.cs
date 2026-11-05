namespace DesktopMotionStudio.Core.IO;

internal enum LocalPathError { None, InvalidPath, Missing, ReparsePoint, IoError }

internal static class LocalPathPolicy
{
    internal static LocalPathError Inspect(string? path, out string fullPath, out FileAttributes attributes)
    {
        fullPath = string.Empty; attributes = default;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal)) return LocalPathError.InvalidPath;
        try
        {
            fullPath = Path.GetFullPath(path);
            string? root = Path.GetPathRoot(fullPath);
            if (root is null || (OperatingSystem.IsWindows() && (root.Length != 3 || root[1] != ':'))) return LocalPathError.InvalidPath;
            if (new DriveInfo(root).DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.CDRom or DriveType.Ram)) return LocalPathError.InvalidPath;
            string current = root;
            attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0) return LocalPathError.ReparsePoint;
            string[] parts = fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (!IsUnambiguousName(parts[i])) return LocalPathError.InvalidPath;
                if ((attributes & FileAttributes.Directory) == 0) return LocalPathError.Missing;
                current = Path.Combine(current, parts[i]);
                // Inspect the final component itself before descending into it.
                attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return LocalPathError.ReparsePoint;
            }
            return LocalPathError.None;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return LocalPathError.Missing; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return LocalPathError.InvalidPath; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return LocalPathError.IoError; }
    }

    internal static bool IsUnambiguousName(string name)
    {
        if (name.Length == 0 || name is "." or ".." || name.StartsWith(' ') || name.EndsWith('.') || name.EndsWith(' ') ||
            name.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c))) return false;
        string stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL") return false;
        return !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(stem[3]));
    }
}
