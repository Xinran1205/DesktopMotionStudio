using System.Diagnostics;
using System.Reflection;
using System.Text;
using DesktopMotionStudio.Core.Shortcuts;

internal static class F10ReceiptChecks
{
    private const string Binding = "既有 Binding 01";

    internal static int Run()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        using var fixture = new Fixture();
        int failures = 0;
        void Check(string name, bool ok) => F09Checks.Check("F10 receipt " + name, ok, ref failures);

        string storage = Path.Combine(fixture.Root, "private", "receipts");
        var store = new ShortcutReceiptStore(storage);
        ShortcutProof current = Proof(fixture.Root, 10);
        ShortcutProof pending = Proof(fixture.Root, 20);
        var receipt = new ShortcutReceipt(1, Binding, current, pending);
        Check("missing is read-only", store.Read(Binding).Status == ShortcutReceiptStatus.Missing && !Directory.Exists(storage));
        Check("invalid ID cannot create a lock", store.Begin(" ", out _) == ShortcutReceiptStatus.Invalid && !Directory.Exists(storage));
        Check("write current and pending proof", Write(store, receipt) == ShortcutReceiptStatus.Ok);
        Check("fresh store recovers both proofs", new ShortcutReceiptStore(storage).Read(Binding).Receipt == receipt);
        Check("legacy casing shares the receipt key", store.Read(Binding.ToUpperInvariant()).Receipt == receipt);

        string records = Path.Combine(storage, "records");
        string path = Directory.GetFiles(records, "*.json").Single();
        byte[] valid = File.ReadAllBytes(path);
        string json = Encoding.UTF8.GetString(valid);
        var cases = new (string Name, string Data, ShortcutReceiptStatus Status)[]
        {
            ("future schema", "{\"schemaVersion\":2,\"future\":true}", ShortcutReceiptStatus.Unsupported),
            ("malformed", "{", ShortcutReceiptStatus.Invalid),
            ("unknown member", json.Insert(1, "\"extra\":null,"), ShortcutReceiptStatus.Invalid),
            ("duplicate schema", json.Insert(1, "\"schemaVersion\":1,"), ShortcutReceiptStatus.Invalid),
            ("duplicate nested member", json.Replace("\"volumeSerial\":1", "\"volumeSerial\":1,\"volumeSerial\":1"), ShortcutReceiptStatus.Invalid),
            ("wrong binding", json.Replace("Binding 01", "Binding 02"), ShortcutReceiptStatus.Invalid),
            ("missing proof", "{\"schemaVersion\":1,\"bindingId\":\"" + Binding + "\"}", ShortcutReceiptStatus.Invalid),
            ("bad hash", json.Replace(new string('A', 64), new string('g', 64)), ShortcutReceiptStatus.Invalid),
            ("too large", json + new string(' ', 16385), ShortcutReceiptStatus.Invalid)
        };
        foreach (var item in cases)
        {
            File.WriteAllText(path, item.Data);
            byte[] original = File.ReadAllBytes(path);
            Check(item.Name + " blocks save and clear and preserves bytes",
                store.Read(Binding).Status == item.Status && Write(store, receipt) == item.Status &&
                Clear(store) == item.Status && File.ReadAllBytes(path).SequenceEqual(original));
        }

        File.WriteAllBytes(path, valid);
        using (var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check("blocked replacement preserves receipt and removes only its own stage",
                Write(store, receipt with { Current = pending, Pending = null }) is ShortcutReceiptStatus.Busy or ShortcutReceiptStatus.IoError &&
                File.ReadAllBytes(path).SequenceEqual(valid) && Directory.GetFiles(records, "*.tmp").Length == 0);
        }
        Check("valid atomic promotion", Write(store, receipt with { Current = pending, Pending = null }) == ShortcutReceiptStatus.Ok &&
            store.Read(Binding).Receipt is { Current: { FileIndex: 20 }, Pending: null });

        string profile = Path.Combine(fixture.Root, "studio-state.json");
        File.WriteAllText(profile, "profile fixture");
        File.WriteAllText(profile + ".bak", "backup fixture");
        using Process worker = StartWorker(storage);
        try
        {
            string? ready = worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult();
            Check("separate worker persisted pending before interruption", ready == "READY");
            var elapsed = Stopwatch.StartNew();
            ShortcutReceiptStatus busy = store.Begin(Binding.ToLowerInvariant(), out var competing);
            competing?.Dispose();
            Check("cross-process lock waits at most about two seconds", busy == ShortcutReceiptStatus.Busy &&
                elapsed.ElapsedMilliseconds is >= 1700 and < 3500);
            worker.Kill();
            Check("worker exited", worker.WaitForExit(5000));
            Check("pending receipt survives abrupt owner death", store.Read(Binding).Receipt is { Current: null, Pending.FileIndex: 30 });
            Check("lock is available after death and explicit clear works", Clear(store) == ShortcutReceiptStatus.Ok &&
                store.Read(Binding).Status == ShortcutReceiptStatus.Missing);
        }
        finally
        {
            if (!worker.HasExited) { worker.Kill(); worker.WaitForExit(5000); }
        }
        Check("profile and backup bytes preserved", File.ReadAllText(profile) == "profile fixture" &&
            File.ReadAllText(profile + ".bak") == "backup fixture");
        return failures;
    }

    internal static int Worker(string storage)
    {
        var store = new ShortcutReceiptStore(storage);
        if (store.Begin(Binding, out var tx) != ShortcutReceiptStatus.Ok) return 3;
        using (tx)
        {
            if (tx!.Save(new(1, Binding, null, Proof(storage, 30))) != ShortcutReceiptStatus.Ok) return 4;
            Console.WriteLine("READY");
            Console.Out.Flush();
            Thread.Sleep(Timeout.Infinite);
        }
        return 0;
    }

    private static ShortcutProof Proof(string root, ulong id) =>
        new(Path.Combine(root, "Fixture.lnk"), Path.Combine(root, "Launcher.exe"), 1, id, new string('A', 64));

    private static ShortcutReceiptStatus Write(ShortcutReceiptStore store, ShortcutReceipt receipt)
    {
        ShortcutReceiptStatus result = store.Begin(Binding, out var tx);
        using (tx) return tx?.Save(receipt) ?? result;
    }

    private static ShortcutReceiptStatus Clear(ShortcutReceiptStore store)
    {
        ShortcutReceiptStatus result = store.Begin(Binding, out var tx);
        using (tx) return tx?.Clear() ?? result;
    }

    private static Process StartWorker(string storage)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
        };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--f10-receipt-worker");
        start.ArgumentList.Add(storage);
        return Process.Start(start) ?? throw new InvalidOperationException("Receipt worker did not start.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("dms-f10-receipts-");
        internal string Root => _directory.FullName;
        public void Dispose()
        {
            string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (string.Equals(_directory.Parent?.FullName, parent, StringComparison.OrdinalIgnoreCase) &&
                _directory.Name.StartsWith("dms-f10-receipts-", StringComparison.Ordinal))
                _directory.Delete(recursive: true);
        }
    }
}
