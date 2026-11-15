using System.Text;
using DesktopMotionStudio.Core.IO;
using Microsoft.Win32.SafeHandles;

namespace DesktopMotionStudio.Core.Distribution;

internal sealed record InstalledSelection(Version Version, string StudioHash, string LauncherHash)
{
    private const string Magic = "DMS1";
    private const int MaxBytes = 160;

    internal byte[] Encode()
    {
        if (Version is null || ParseVersion(Version.ToString()) is null || !IsDigest(StudioHash) || !IsDigest(LauncherHash))
            throw new InvalidDataException("Invalid installed version or entry digest.");
        return Encoding.ASCII.GetBytes($"{Magic}\n{Version}\n{StudioHash}\n{LauncherHash}\n");
    }

    internal static InstalledSelection Read(SafeFileHandle file)
    {
        var info = WindowsFileScope.ReadInfo(file);
        if (!info.IsSingleRegularFile || info.Length is 0 or > MaxBytes)
            throw new InvalidDataException("Invalid installed selector file.");
        Span<byte> bytes = stackalloc byte[(int)info.Length];
        int offset = 0;
        while (offset < bytes.Length)
        {
            int read = RandomAccess.Read(file, bytes[offset..], offset);
            if (read == 0) throw new InvalidDataException("Incomplete installed selector.");
            offset += read;
        }
        string[] fields = Encoding.ASCII.GetString(bytes).Split('\n');
        if (fields is not [Magic, var version, var studio, var launcher, ""] ||
            ParseVersion(version) is not Version parsed || !IsDigest(studio) || !IsDigest(launcher))
            throw new InvalidDataException("Unsupported or invalid installed selector.");
        return new(parsed, studio, launcher);
    }

    internal static Version? ParseVersion(string value) =>
        value.Length is >= 5 and <= 17 && Version.TryParse(value, out var parsed) &&
        parsed.Build is >= 0 and <= 99999 && parsed.Major <= 99999 && parsed.Minor <= 99999 &&
        parsed.Revision < 0 && value == parsed.ToString(3) ? parsed : null;

    internal static bool IsDigest(string? value) =>
        value is { Length: 64 } && value.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'F');
}
