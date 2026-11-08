using System.Text;

namespace DesktopMotionStudio.Core.Localization;

public sealed class StudioLanguagePreference(string profilePath)
{
    private readonly string _path = Path.GetFullPath(profilePath) + ".language";

    public string Read()
    {
        try
        {
            using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            Span<byte> bytes = stackalloc byte[16];
            int count = file.ReadAtLeast(bytes, bytes.Length, false);
            if (count < bytes.Length)
            {
                string language = Encoding.UTF8.GetString(bytes[..count]).Trim();
                if (ProductText.IsSupported(language)) return language;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return ProductText.DefaultLanguage;
    }

    public bool TrySave(string language)
    {
        if (!ProductText.IsSupported(language)) return false;
        string pending = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(Encoding.UTF8.GetBytes(language));
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
