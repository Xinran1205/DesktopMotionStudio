using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.Launch;

namespace DesktopMotionStudio.Studio;

internal static class DiscoveryWorker
{
    internal static int Run()
    {
        ApplicationDiscoveryResult result = Scan([
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)]);
        byte[] output;
        try { output = ApplicationDiscoveryProtocol.Encode(result); }
        catch (InvalidDataException) { output = ApplicationDiscoveryProtocol.Encode(new(ApplicationDiscoveryStatus.Failed, [])); }
        using Stream stdout = Console.OpenStandardOutput();
        stdout.Write(output);
        stdout.Flush();
        return 0;
    }

    internal static ApplicationDiscoveryResult Scan(IReadOnlyList<string> roots, bool includePackaged = true)
    {
        try
        {
            var candidates = new List<ApplicationCandidate>();
            foreach (string root in roots) AddLinks(root, candidates);
            if (includePackaged) AddPackaged(candidates);
            ApplicationCandidate[] sorted = candidates
                .DistinctBy(item => (item.Target.Kind, item.Target.Identifier))
                .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
            return new(sorted.Length == 0 ? ApplicationDiscoveryStatus.Empty : ApplicationDiscoveryStatus.Complete, sorted);
        }
        catch (Exception) { return new(ApplicationDiscoveryStatus.Failed, []); }
    }

    private static void AddLinks(string root, List<ApplicationCandidate> results)
    {
        if (!LaunchTargetIdentityResolver.IsLocalPath(root) || !Directory.Exists(root)) return;
        var pending = new Stack<string>();
        pending.Push(root);
        int visited = 0;
        while (pending.TryPop(out string? directory))
        {
            if (++visited > 2000) throw new InvalidDataException("Discovery directory limit.");
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (string child in Directory.EnumerateDirectories(directory))
                {
                    if (pending.Count >= 2000) throw new InvalidDataException("Discovery directory limit.");
                    pending.Push(child);
                }
                foreach (string link in Directory.EnumerateFiles(directory, "*.lnk"))
                {
                    if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0) continue;
                    Add(results, Path.GetFileNameWithoutExtension(link), new(ApplicationTargetKind.Shortcut, link, []), "StartMenu");
                }
            }
            catch (Exception ex) when (ex is not InvalidDataException && ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void AddPackaged(List<ApplicationCandidate> results)
    {
        object? shell = null;
        object? folder = null;
        object? items = null;
        try
        {
            Type type = Type.GetTypeFromProgID("Shell.Application") ?? throw new InvalidOperationException("Shell unavailable.");
            shell = Activator.CreateInstance(type)!;
            folder = ((dynamic)shell).NameSpace("shell:::{4234d49b-0245-4df3-b780-3893943456e1}");
            if (folder is null) return;
            items = ((dynamic)folder).Items();
            int count = ((dynamic)items).Count;
            if (count > 10_000) throw new InvalidDataException("AppsFolder limit.");
            for (int i = 0; i < count; i++)
            {
                object? item = null;
                try
                {
                    item = ((dynamic)items).Item(i);
                    string id = ((dynamic)item!).Path;
                    if (!IsCurrentUserPackaged(id)) continue;
                    string name = ((dynamic)item).Name;
                    Add(results, name, new(ApplicationTargetKind.Packaged, id, []), "Packaged");
                }
                catch (Exception ex) when (ex is COMException or ArgumentException) { }
                finally { Release(item); }
            }
        }
        finally { Release(items); Release(folder); Release(shell); }
    }

    private static void Add(List<ApplicationCandidate> results, string name, ApplicationTargetDefinition target, string source)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || name.Any(char.IsControl) || !target.IsValid) return;
        if (results.Count >= ApplicationDiscoveryProtocol.MaximumCandidates) throw new InvalidDataException("Candidate limit.");
        results.Add(new(name, target, source));
    }

    private static bool IsCurrentUserPackaged(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 129) return false;
        uint familyLength = 0, appLength = 0;
        if (ParseApplicationUserModelId(id, ref familyLength, null, ref appLength, null) != 122 ||
            familyLength is 0 or > 256 || appLength is 0 or > 256) return false;
        var family = new StringBuilder((int)familyLength);
        var app = new StringBuilder((int)appLength);
        if (ParseApplicationUserModelId(id, ref familyLength, family, ref appLength, app) != 0) return false;
        uint count = 0, chars = 0;
        int status = GetPackagesByPackageFamily(family.ToString(), ref count, 0, ref chars, 0);
        return status is 0 or 122 && count > 0;
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int ParseApplicationUserModelId(string id, ref uint familyLength, StringBuilder? family, ref uint appLength, StringBuilder? app);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackagesByPackageFamily(string family, ref uint count, nint packages, ref uint chars, nint buffer);
}
