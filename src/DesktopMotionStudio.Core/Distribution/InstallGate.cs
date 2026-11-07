using System.Security.Cryptography;
using System.Text;

namespace DesktopMotionStudio.Core.Distribution;

internal sealed class InstallGate : IDisposable
{
    private readonly Mutex _mutex;
    internal bool Entered { get; }

    internal InstallGate(string root) : this(root, "Setup", TimeSpan.Zero) { }

    internal static InstallGate ForEntry(string root) => new(root, "Entry", TimeSpan.FromSeconds(30));

    private InstallGate(string root, string kind, TimeSpan wait)
    {
        string token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())));
        _mutex = new Mutex(false, $@"Global\DesktopMotionStudio.{kind}.{token}");
        try { Entered = _mutex.WaitOne(wait); }
        catch (AbandonedMutexException) { Entered = true; }
        catch { _mutex.Dispose(); throw; }
    }

    public void Dispose()
    {
        if (Entered) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
