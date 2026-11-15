using System.ComponentModel;
using System.Diagnostics;

namespace DesktopMotionStudio.Core.Shortcuts;

internal sealed record ShortcutCleanupResult(int Removed, int Preserved, bool Incomplete)
{
    internal static ShortcutCleanupResult Unavailable { get; } = new(0, 0, true);
}

internal sealed class ManagedShortcutCleanup(string receiptDirectory, string launcher, Func<string> desktop)
{
    internal ShortcutCleanupResult Run()
    {
        var store = new ShortcutReceiptStore(receiptDirectory);
        int removed = 0, preserved = 0;
        try
        {
            IReadOnlyList<ShortcutReceiptRead> candidates = store.Discover();
            if (candidates.Count == 0) return new(0, 0, false);
            using var scope = WindowsShortcutPlatform.Open(desktop());
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (timer.Elapsed > TimeSpan.FromSeconds(10) || !IsCurrent(scope))
                    return new(removed, preserved + candidates.Count - i, true);
                ShortcutReceiptRead candidate = candidates[i];
                if (candidate is { Status: ShortcutReceiptStatus.Ok, Receipt.BindingId: string id } &&
                    RemoveVerified(store, scope, id)) removed++;
                else preserved++;
            }
            return new(removed, preserved, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidDataException)
        {
            return new(removed, preserved, true);
        }
    }

    private bool RemoveVerified(ShortcutReceiptStore store, WindowsShortcutPlatform scope, string id)
    {
        if (store.Begin(id, out var transaction, wait: false) != ShortcutReceiptStatus.Ok) return false;
        using (transaction)
        {
            ShortcutReceiptRead read = transaction!.Read();
            if (read is not { Status: ShortcutReceiptStatus.Ok, Receipt: not null }) return false;
            ShortcutProof? current = InScope(read.Receipt.Current, scope);
            ShortcutProof? pending = InScope(read.Receipt.Pending, scope);
            if ((pending ?? current) is not ShortcutProof proof) return false;
            string name = Path.GetFileName(proof.Path);
            try
            {
                using (var entry = scope.Inspect(name, current, pending, forDelete: true))
                {
                    if (entry.State != ShortcutFileState.Owned || !IsCurrent(scope)) return false;
                    entry.Delete();
                }
                using var after = scope.Inspect(name);
                if (after.State != ShortcutFileState.Missing) return false;
                // When a second proof belongs to a different installation, preserve its evidence.
                if ((read.Receipt.Current is null || current is not null) && (read.Receipt.Pending is null || pending is not null))
                    return transaction.Clear() == ShortcutReceiptStatus.Ok;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidDataException) { return false; }
        }
    }

    private bool IsCurrent(WindowsShortcutPlatform scope) =>
        string.Equals(desktop(), scope.Root, StringComparison.OrdinalIgnoreCase);

    private ShortcutProof? InScope(ShortcutProof? proof, WindowsShortcutPlatform scope) =>
        proof is not null && string.Equals(Path.GetDirectoryName(proof.Path), scope.Root, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(proof.LauncherPath, launcher, StringComparison.OrdinalIgnoreCase) ? proof : null;
}
