using System.Diagnostics;
using System.Text;
using DesktopMotionStudio.Core.Diagnostics;

internal static class F11DiagnosticChecks
{
    internal static int Run()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        DirectoryInfo fixture = Directory.CreateTempSubdirectory("dms-f11-logs-");
        int failures = 0;
        void Check(string name, bool passed) => F09Checks.Check("F11 diagnostics " + name, passed, ref failures);
        try { RunCases(fixture.FullName, Check); }
        finally
        {
            string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (string.Equals(fixture.Parent?.FullName, temp, StringComparison.OrdinalIgnoreCase) &&
                fixture.Name.StartsWith("dms-f11-logs-", StringComparison.Ordinal))
                fixture.Delete(recursive: true);
        }
        return failures;
    }

    private static void RunCases(string root, Action<string, bool> check)
    {
        string logs = Path.Combine(root, "Logs");
        string records = Path.Combine(logs, "records");
        string primary = Path.Combine(records, "events.json");
        string previous = Path.Combine(records, "events.previous");
        string next = Path.Combine(records, "events.next");
        var store = new DiagnosticLogStore(logs);
        DiagnosticEvent first = Sample();
        DiagnosticEvent second = first with { Stage = DiagnosticStage.Animation, Result = DiagnosticResultCode.HandedOff };
        check("empty read creates no directories", store.Read().Status == DiagnosticStatus.Empty && !Directory.Exists(logs));
        check("invalid event cannot create storage", store.Append(first with { ElapsedMilliseconds = 120001 }) == DiagnosticStatus.Invalid &&
            store.Append(first with { Stage = (DiagnosticStage)255 }) == DiagnosticStatus.Invalid && !Directory.Exists(logs));
        check("correlated terminal records commit together",
            store.Append(first, second) == DiagnosticStatus.Stored && store.Read().Events.SequenceEqual([first, second]));

        string lockPath = Path.Combine(logs, "locks", "events.lock");
        byte[] baseline = File.ReadAllBytes(primary);
        using (FileStream gate = File.Open(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var time = Stopwatch.StartNew();
            check("busy writer drops immediately without changing records",
                store.Append(Sample()) == DiagnosticStatus.Busy && time.Elapsed < TimeSpan.FromSeconds(1) &&
                File.ReadAllBytes(primary).SequenceEqual(baseline));
            check("read reports busy instead of reading across a commit", store.Read().Status == DiagnosticStatus.Busy);
        }
        check("read returns the same history after releasing the lock", store.Read().Events.SequenceEqual([first, second]));

        var accepted = new DiagnosticEvent[144];
        for (int i = 0; i < accepted.Length; i++)
        {
            // Deliberately reverse wall-clock timestamps. Retention follows commit order.
            accepted[i] = Sample() with { TimestampUtc = first.TimestampUtc.AddSeconds(-i), ElapsedMilliseconds = i };
            if (store.Append(accepted[i]) != DiagnosticStatus.Stored)
                throw new IOException($"Could not append retention fixture {i}.");
        }
        DiagnosticSnapshot retained = store.Read();
        check("retention is bounded and ordered by append",
            retained is { Status: DiagnosticStatus.Ready, Events.Count: > 0 and <= 128 } &&
            retained.Events.SequenceEqual(accepted.TakeLast(retained.Events.Count)));
        FileInfo[] files = new DirectoryInfo(records).GetFiles();
        check("product generations stay within committed byte bounds",
            files.Length == 2 && files.All(file => file.Length <= 32 * 1024) &&
            files.Sum(file => file.Length) <= 64 * 1024 && !File.Exists(next));

        baseline = File.ReadAllBytes(primary);
        byte[] backup = File.ReadAllBytes(previous);
        const string secret = "PRIVATE-TOKEN-with-USER-PATH";
        string encoded = Encoding.UTF8.GetString(baseline);
        check("share formatter contains typed stage and no path", DiagnosticReport.Format(retained.Events).Contains("Target", StringComparison.Ordinal) &&
            !DiagnosticReport.Format(retained.Events).Contains(root, StringComparison.Ordinal));
        var invalidCases = new (string Text, DiagnosticStatus Status)[]
        {
            (encoded.Insert(1, $"\"rawException\":\"{secret}\","), DiagnosticStatus.Invalid),
            (encoded.Insert(1, "\"schemaVersion\":1,"), DiagnosticStatus.Invalid),
            ("{\"schemaVersion\":9,\"events\":[]}", DiagnosticStatus.Unsupported),
            (encoded + new string(' ', 33 * 1024), DiagnosticStatus.Invalid)
        };
        foreach (var item in invalidCases)
        {
            File.WriteAllText(primary, item.Text);
            check("invalid/future envelope is preserved " + item.Status,
                store.Read().Status == item.Status && store.Append(Sample()) == item.Status &&
                File.ReadAllText(primary) == item.Text && File.ReadAllBytes(previous).SequenceEqual(backup));
        }

        File.WriteAllBytes(primary, baseline);
        File.Move(primary, previous, overwrite: true);
        check("interrupted handover reads previous without creating primary",
            store.Read().Events.SequenceEqual(retained.Events) && !File.Exists(primary));
        DiagnosticEvent newest = Sample();
        check("first append after interrupted handover restores the current generation",
            store.Append(newest) == DiagnosticStatus.Stored && File.Exists(primary) && store.Read().Events[^1] == newest);
        File.WriteAllText(next, "partial stage");
        check("corrupt abandoned stage is retained and blocks writes",
            store.Read().Status == DiagnosticStatus.Invalid && store.Append(newest) == DiagnosticStatus.Invalid &&
            File.ReadAllText(next) == "partial stage");
        File.Delete(next);

        DiagnosticEvent[] parallel = Enumerable.Range(0, 24).Select(_ => Sample()).ToArray();
        var outcomes = new DiagnosticStatus[parallel.Length];
        Parallel.For(0, parallel.Length, i => outcomes[i] = store.Append(parallel[i]));
        DiagnosticSnapshot after = store.Read();
        check("concurrent commits are retained and contention is explicit",
            outcomes.Contains(DiagnosticStatus.Stored) && outcomes.All(status => status is DiagnosticStatus.Stored or DiagnosticStatus.Busy) &&
            parallel.Where((_, i) => outcomes[i] == DiagnosticStatus.Stored).All(item => after.Events.Contains(item)));

        string redirect = Path.Combine(root, "Redirect");
        F09Checks.CreateJunction(redirect, logs);
        try
        {
            byte[] exact = File.ReadAllBytes(primary);
            check("reparse log root is unavailable and preserves records",
                new DiagnosticLogStore(redirect).Append(Sample()) == DiagnosticStatus.Unavailable &&
                File.ReadAllBytes(primary).SequenceEqual(exact));
        }
        finally { Directory.Delete(redirect, recursive: false); }
    }

    private static DiagnosticEvent Sample() => new(Guid.NewGuid(), DateTimeOffset.UtcNow, new(1, 2, 3, 4), 26200,
        DiagnosticAdapter.Executable, DiagnosticStage.Target, DiagnosticResultCode.WindowReady, DiagnosticFallback.None, 23);
}
