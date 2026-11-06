using System.ComponentModel;
using System.Diagnostics;
using DesktopMotionStudio.Core.IO;

if (args is ["--read", var readPath])
{
    if (!IsFixture(readPath)) return 2;
    try { Console.WriteLine((char)ReadActive(readPath)); return 0; }
    catch (Exception ex) when (ex is InvalidDataException or Win32Exception) { return 3; }
}

if (args is ["--writer", var directory, var pause])
{
    // The writer only receives a fixture location. It publishes no product state.
    if (!IsFixture(directory)) return 2;
    using var scope = WindowsFileScope.Open(directory);
    using var stage = scope.OpenChild("active.next",
        WindowsFileScope.ReadAccess | WindowsFileScope.WriteAccess | WindowsFileScope.DeleteAccess,
        disposition: WindowsFileScope.CreateNew);
    using var stream = new FileStream(stage, FileAccess.ReadWrite);
    stream.WriteByte((byte)'B');
    stream.Flush(flushToDisk: true);
    Pause("staged");
    using (var active = scope.OpenChild("active", WindowsFileScope.ReadAccess | WindowsFileScope.DeleteAccess))
        scope.Publish(active, "active.previous");
    Pause("retired");
    try { scope.Publish(stage, "active"); }
    catch (Win32Exception ex) when (ex.NativeErrorCode is 80 or 183)
    {
        Console.WriteLine("conflict");
        return 3;
    }
    Pause("committed");
    return 0;

    void Pause(string phase)
    {
        if (phase != pause) return;
        Console.WriteLine(phase);
        if (Console.ReadLine() != "resume") Environment.Exit(2);
    }
}

foreach ((string phase, byte expected) in new[]
{
    ("staged", (byte)'A'),
    ("retired", (byte)'A'),
    ("committed", (byte)'B')
})
{
    string root = NewFixture();
    using Process worker = StartWriter(root, phase);
    try
    {
        await ExpectLine(worker, phase);
        worker.Kill();
        await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Check(ReadActive(root) == expected, $"{phase} process death leaves a complete {(char)expected} selector");
        Check(File.ReadAllText(Path.Combine(root, "A", "payload.txt")) == "old" &&
            File.ReadAllText(Path.Combine(root, "B", "payload.txt")) == "new", $"{phase} preserves both generation payloads");
    }
    finally { if (!worker.HasExited) worker.Kill(); }
}

string conflict = NewFixture();
using (Process worker = StartWriter(conflict, "retired"))
{
    try
    {
        await ExpectLine(worker, "retired");
        File.WriteAllText(Path.Combine(conflict, "active"), "foreign");
        worker.StandardInput.WriteLine("resume");
        await ExpectLine(worker, "conflict");
        await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Check(worker.ExitCode == 3 && File.ReadAllText(Path.Combine(conflict, "active")) == "foreign" &&
            File.ReadAllText(Path.Combine(conflict, "active.previous")) == "A",
            "an unexpected selector occupant is preserved");
        try { ReadActive(conflict); throw new Exception("Invalid primary was accepted."); }
        catch (InvalidDataException) { Console.WriteLine("PASS malformed primary fails closed"); }
    }
    finally { if (!worker.HasExited) worker.Kill(); }
}
return 0;

static bool IsFixture(string directory)
{
    string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
    return WindowsFileScope.IsCanonicalLocalPath(directory) &&
        string.Equals(Path.GetDirectoryName(directory), temp, StringComparison.OrdinalIgnoreCase) &&
        Path.GetFileName(directory).StartsWith("dms-f12-selector-", StringComparison.Ordinal);
}

static string NewFixture()
{
    string directory = Directory.CreateTempSubdirectory("dms-f12-selector-").FullName;
    foreach ((string leaf, string contents) in new[] { ("A", "old"), ("B", "new") })
    {
        string generation = Directory.CreateDirectory(Path.Combine(directory, leaf)).FullName;
        File.WriteAllText(Path.Combine(generation, "payload.txt"), contents);
    }
    File.WriteAllText(Path.Combine(directory, "active"), "A");
    return directory;
}

static Process StartWriter(string path, string pause)
{
    var info = new ProcessStartInfo(Environment.ProcessPath!)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardInput = true,
        RedirectStandardOutput = true
    };
    foreach (string arg in new[] { "--writer", path, pause }) info.ArgumentList.Add(arg);
    return Process.Start(info) ?? throw new InvalidOperationException("Could not start selector writer.");
}

static async Task ExpectLine(Process worker, string expected)
{
    string? observed = await worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
    if (observed != expected) throw new InvalidOperationException($"Expected {expected}, observed {observed}.");
}

static int ReadActive(string path)
{
    using var scope = WindowsFileScope.Open(path);
    try { return Read("active"); }
    catch (Win32Exception ex) when (WindowsFileScope.IsMissing(ex)) { return Read("active.previous"); }

    int Read(string name)
    {
        using var handle = scope.OpenChild(name, WindowsFileScope.ReadAccess, share: 7);
        var stat = WindowsFileScope.ReadInfo(handle);
        if (!stat.IsSingleRegularFile || stat.Length != 1) throw new InvalidDataException("Invalid selector file.");
        using var stream = new FileStream(handle, FileAccess.Read);
        int value = stream.ReadByte();
        return value is 'A' or 'B' ? value : throw new InvalidDataException("Invalid selector value.");
    }
}

static void Check(bool passed, string description)
{
    if (!passed) throw new InvalidOperationException(description);
    Console.WriteLine("PASS " + description);
}
