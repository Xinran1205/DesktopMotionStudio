using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace DesktopMotionStudio.Core.Distribution;

[SupportedOSPlatform("windows")]
internal sealed class InstallRegistration(RegistryKey parent)
{
    private const string KeyName = "DesktopMotionStudio";
    private const int QueryAndSet = 0x0001 | 0x0002;

    internal bool Exists()
    {
        using RegistryKey? key = parent.OpenSubKey(KeyName);
        return key is not null;
    }

    internal void VerifyRemovable(InstallOwner owner, string root, Version version)
    {
        using SafeRegistryHandle? handle = OpenRemovalHandle(owner, root, version);
    }

    internal void Remove(InstallOwner owner, string root, Version version)
    {
        using SafeRegistryHandle? handle = OpenRemovalHandle(owner, root, version);
        if (handle is null) return;
        int status = NtDeleteKey(handle);
        if (status < 0) throw new Win32Exception(RtlNtStatusToDosError(status));
    }

    private SafeRegistryHandle? OpenRemovalHandle(InstallOwner owner, string root, Version version)
    {
        const int delete = 0x00010000, enumerate = 0x0008, openLink = 0x00000008;
        int status = RegOpenKeyExW(parent.Handle, KeyName, openLink, delete | enumerate | 0x0001, out var handle);
        if (status != 0)
        {
            handle.Dispose();
            if (status == 2) return null;
            throw new Win32Exception(status);
        }
        try
        {
            if (!IsOwned(handle, owner)) throw new InvalidDataException("The Windows registration is foreign.");
            // A second non-owning wrapper keeps the verified native handle available for deletion.
            using var view = new SafeRegistryHandle(handle.DangerousGetHandle(), ownsHandle: false);
            using RegistryKey key = RegistryKey.FromHandle(view);
            var expected = Values(root, version);
            if (key.SubKeyCount != 0 || key.ValueCount != expected.Length ||
                expected.Any(item => !Equals(key.GetValue(item.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), item.Value) ||
                    key.GetValueKind(item.Name) != item.Kind))
                throw new InvalidDataException("The Windows registration changed.");
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    internal void Ensure(InstallOwner owner, string root, Version version, Action? keyClaimed = null,
        Version? previousVersion = null)
    {
        string claim = ClassName(owner);
        int status = RegOpenKeyExW(parent.Handle, KeyName, 8, QueryAndSet, out var handle);
        if (status == 2)
        {
            handle.Dispose();
            status = RegCreateKeyExW(parent.Handle, KeyName, 0, claim, 0, QueryAndSet, 0, out handle, out _);
        }
        using (handle)
        {
            if (status != 0) throw new Win32Exception(status);
            if (!IsOwned(handle, owner)) throw new InvalidDataException("The Windows registration belongs to another installation.");
            keyClaimed?.Invoke();
            using RegistryKey key = RegistryKey.FromHandle(handle);
            var values = Values(root, version);
            foreach (var (name, value, kind) in values)
            {
                object? current = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (current is null) continue;
                bool priorVersion = name == "DisplayVersion" && previousVersion is not null &&
                    Equals(current, previousVersion.ToString(3));
                if ((!Equals(current, value) && !priorVersion) || key.GetValueKind(name) != kind)
                    throw new InvalidDataException("The Windows registration was modified.");
            }
            foreach (var (name, value, kind) in values)
                if (!Equals(key.GetValue(name, null), value)) key.SetValue(name, value, kind);
            key.Flush();
        }
    }

    private static (string Name, object Value, RegistryValueKind Kind)[] Values(string root, Version version)
    {
        string studio = Path.Combine(root, InstalledLayout.StudioName);
        return
        [
            ("InstallLocation", root, RegistryValueKind.String),
            ("UninstallString", $"\"{studio}\" --uninstall", RegistryValueKind.String),
            ("DisplayIcon", studio, RegistryValueKind.String),
            ("DisplayVersion", version.ToString(3), RegistryValueKind.String),
            ("Publisher", "Desktop Motion Studio", RegistryValueKind.String),
            ("NoModify", 1, RegistryValueKind.DWord),
            ("NoRepair", 1, RegistryValueKind.DWord),
            ("DisplayName", "Desktop Motion Studio", RegistryValueKind.String)
        ];
    }

    private static string ClassName(InstallOwner owner) => $"DesktopMotionStudio-{owner.Id:N}";

    private static bool IsOwned(SafeRegistryHandle key, InstallOwner owner)
    {
        var actual = new StringBuilder(96);
        uint length = (uint)actual.Capacity;
        int status = RegQueryInfoKeyW(key, actual, ref length, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        return status == 0 && actual.ToString() == ClassName(owner);
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RegCreateKeyExW(SafeRegistryHandle key, string subkey, uint reserved,
        string keyClass, uint options, int access, nint security, out SafeRegistryHandle result, out uint disposition);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RegOpenKeyExW(SafeRegistryHandle key, string subkey, int options, int access, out SafeRegistryHandle result);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtDeleteKey(SafeRegistryHandle key);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int RtlNtStatusToDosError(int status);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RegQueryInfoKeyW(SafeRegistryHandle key, StringBuilder keyClass, ref uint classLength,
        nint reserved, nint subkeys, nint maxSubkey, nint maxClass, nint values, nint maxValueName,
        nint maxValue, nint securityDescriptor, nint lastWriteTime);
}
