using System.Security.Cryptography;
using System.Text;

namespace DesktopMotionStudio.Core.Distribution;

internal sealed class InstallGate : IDisposable
{
    private readonly Mutex _mutex;
    internal bool Entered { get; }

    internal InstallGate(string root)
    {
        string token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())));
        _mutex = new Mutex(false, $@"Global\DesktopMotionStudio.Setup.{token}");
        try { Entered = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { Entered = true; }
        catch { _mutex.Dispose(); throw; }
    }

    public void Dispose()
    {
        if (Entered) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
