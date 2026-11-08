using System.Text;

namespace DesktopMotionStudio.Core.State;

internal sealed class ProfilePreferenceFile(string profilePath, string suffix)
{
    private readonly string _path = Path.GetFullPath(profilePath) + suffix;

    internal string? Read()
    {
        try
        {
            using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            Span<byte> bytes = stackalloc byte[16];
            int count = file.ReadAtLeast(bytes, bytes.Length, false);
            return count < bytes.Length ? Encoding.UTF8.GetString(bytes[..count]).Trim() : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    internal bool TrySave(string value)
    {
        string pending = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(Encoding.UTF8.GetBytes(value));
                file.Flush(flushToDisk: true);
            }
            File.Move(pending, _path, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
        finally
        {
            try { File.Delete(pending); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
