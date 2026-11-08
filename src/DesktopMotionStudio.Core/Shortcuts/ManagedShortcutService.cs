using System.ComponentModel;
using System.Runtime.InteropServices;
using DesktopMotionStudio.Core.IO;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.State;

namespace DesktopMotionStudio.Core.Shortcuts;

public enum ShortcutStatus
{
    NotCreated, Healthy, Missing, RepairRequired, OutOfScope, Conflict,
    Busy, Unavailable, StorageError, Ineligible, Created, Removed
}

public sealed record ShortcutResult(ShortcutStatus Status, string? Path = null);

public sealed class ManagedShortcutService(
    IStudioStateStore state, string catalogRoot, string receiptDirectory, string launcherPath, Func<string> desktop)
{
    private readonly ShortcutReceiptStore _receipts = new(receiptDirectory);
    private readonly ManagedLaunchRequest _requests = new(state, catalogRoot);

    public ShortcutResult Inspect(string bindingId) => Run(bindingId, Operation.Inspect);
    public ShortcutResult Create(string bindingId) => Run(bindingId, Operation.Create);
    public ShortcutResult Repair(string bindingId) => Run(bindingId, Operation.Repair);
    public ShortcutResult Remove(string bindingId) => Run(bindingId, Operation.Remove);

    private ShortcutResult Run(string bindingId, Operation operation)
    {
        if (ManagedLaunchRequest.EncodeBindingId(bindingId) is null) return new(ShortcutStatus.Ineligible);
        try
        {
            if (operation == Operation.Inspect)
            {
                ShortcutReceiptRead snapshot = _receipts.Read(bindingId);
                if (snapshot.Status is not (ShortcutReceiptStatus.Ok or ShortcutReceiptStatus.Missing))
                    return new(Map(snapshot.Status));
                using var desktopScope = WindowsShortcutPlatform.Open(desktop());
                return Operate(desktopScope, bindingId, operation, snapshot.Receipt, null);
            }

            ShortcutReceiptStatus started = _receipts.Begin(bindingId, out var transaction);
            if (started != ShortcutReceiptStatus.Ok) return new(Map(started));
            using (transaction)
            {
                ShortcutReceiptRead current = transaction!.Read();
                if (current.Status is not (ShortcutReceiptStatus.Ok or ShortcutReceiptStatus.Missing))
                    return new(Map(current.Status));
                using var scope = WindowsShortcutPlatform.Open(desktop());
                return Operate(scope, bindingId, operation, current.Receipt, transaction);
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 80 or 183) { return new(ShortcutStatus.Conflict); }
        catch (Win32Exception ex) when (WindowsFileScope.IsSharing(ex)) { return new(ShortcutStatus.Busy); }
        catch (Exception ex) when (ex is IOException or Win32Exception or COMException or UnauthorizedAccessException or InvalidDataException)
        { return new(ShortcutStatus.Unavailable); }
    }

    private ShortcutResult Operate(WindowsShortcutPlatform scope, string bindingId, Operation operation,
        ShortcutReceipt? receipt, ShortcutReceiptStore.Transaction? transaction)
    {
        ShortcutProof? current = InScope(receipt?.Current, scope.Root);
        ShortcutProof? pending = InScope(receipt?.Pending, scope.Root);
        ShortcutProof? recorded = pending ?? current;
        bool relocated = receipt is not null && recorded is null;
        if (relocated && operation != Operation.Repair) return new(ShortcutStatus.OutOfScope);
        if (receipt is null && operation == Operation.Remove) return new(ShortcutStatus.NotCreated);

        ManagedLaunchPlan? plan = null;
        string name;
        if (recorded is not null)
            name = Path.GetFileName(recorded.Path);
        else
        {
            plan = _requests.Resolve(bindingId);
            if (plan.Status == ManagedLaunchStatus.StorageError) return new(Map(plan.StorageError));
            if (plan.DisplayName is null)
                return new(operation == Operation.Inspect ? ShortcutStatus.NotCreated : ShortcutStatus.Ineligible);
            name = PreferredName(scope.Root, plan.DisplayName, bindingId);
        }

        string path = Path.Combine(scope.Root, name);
        using var observed = scope.Inspect(name, current, pending, forDelete: operation is Operation.Remove or Operation.Repair);
        ShortcutStatus health = observed.State switch
        {
            ShortcutFileState.Owned => string.Equals(observed.Proof!.LauncherPath, launcherPath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(launcherPath) && LaunchTargetIdentityResolver.IsLocalPath(launcherPath)
                ? ShortcutStatus.Healthy : ShortcutStatus.RepairRequired,
            ShortcutFileState.Missing => receipt is null ? ShortcutStatus.NotCreated : ShortcutStatus.Missing,
            ShortcutFileState.Conflict => ShortcutStatus.Conflict,
            ShortcutFileState.Busy => ShortcutStatus.Busy,
            _ => ShortcutStatus.Unavailable
        };

        if (operation == Operation.Inspect || health is ShortcutStatus.Conflict or ShortcutStatus.Busy or ShortcutStatus.Unavailable)
            return new(health, path);

        if (operation == Operation.Remove)
        {
            if (!IsCurrent(scope)) return new(ShortcutStatus.OutOfScope);
            if (observed.State == ShortcutFileState.Owned)
            {
                observed.Delete();
                observed.Dispose();
                using var after = scope.Inspect(name);
                if (after.State != ShortcutFileState.Missing)
                    return new(after.State == ShortcutFileState.Conflict ? ShortcutStatus.Conflict : ShortcutStatus.Unavailable, path);
            }
            ShortcutReceiptStatus cleared = transaction!.Clear();
            if (cleared != ShortcutReceiptStatus.Ok) return new(Map(cleared), path);
            return transaction.Read().Status == ShortcutReceiptStatus.Missing
                ? new(ShortcutStatus.Removed, path) : new(ShortcutStatus.StorageError, path);
        }

        if (health == ShortcutStatus.Healthy ||
            operation == Operation.Create && receipt is not null ||
            operation == Operation.Repair && receipt is null)
            return new(health, path);

        plan ??= _requests.Resolve(bindingId);
        if (plan.Status == ManagedLaunchStatus.StorageError) return new(Map(plan.StorageError), path);
        if (plan.Status != ManagedLaunchStatus.Ready || plan.PlaybackStatus != ManagedPlaybackStatus.Ready)
            return new(ShortcutStatus.Ineligible, path);
        ShortcutProof stagedProof;
        using (var stage = scope.Stage(name, launcherPath, bindingId, (plan.Target as ExecutableTarget)?.Path))
        {
            if (!IsCurrent(scope)) return new(ShortcutStatus.OutOfScope, path);
            stagedProof = stage.Proof;
            // The identity transition keeps profile bytes intact while serializing the
            // final eligibility check and publication with binding/application edits.
            ShortcutStatus commit = ShortcutStatus.Ineligible;
            RepositoryResult locked = state.Update(snapshot =>
            {
                ManagedLaunchPlan latest = _requests.Resolve(bindingId, snapshot);
                if (latest.Status != ManagedLaunchStatus.Ready || latest.PlaybackStatus != ManagedPlaybackStatus.Ready ||
                    !string.Equals(latest.AppId, plan.AppId, StringComparison.OrdinalIgnoreCase))
                    return snapshot;
                ShortcutReceiptStatus saved = transaction!.Save(new(1, bindingId, observed.Proof ?? current, stagedProof));
                if (saved != ShortcutReceiptStatus.Ok)
                {
                    commit = Map(saved);
                    return snapshot;
                }
                if (!IsCurrent(scope))
                {
                    commit = ShortcutStatus.OutOfScope;
                    return snapshot;
                }
                if (observed.State == ShortcutFileState.Owned)
                {
                    observed.Delete();
                    observed.Dispose();
                }
                stage.Publish();
                commit = ShortcutStatus.Created;
                return snapshot;
            });
            if (!locked.IsSuccess) return new(Map(locked.Error), path);
            if (commit != ShortcutStatus.Created) return new(commit, path);
        }

        using var confirmed = scope.Inspect(name, stagedProof);
        if (confirmed.State != ShortcutFileState.Owned)
            return new(confirmed.State == ShortcutFileState.Conflict ? ShortcutStatus.Conflict : ShortcutStatus.Unavailable, path);
        ShortcutReceiptStatus promoted = transaction!.Save(new(1, bindingId, stagedProof));
        return new(promoted == ShortcutReceiptStatus.Ok ? ShortcutStatus.Created : Map(promoted), path);
    }

    private bool IsCurrent(WindowsShortcutPlatform scope) =>
        string.Equals(desktop(), scope.Root, StringComparison.OrdinalIgnoreCase);

    private static ShortcutProof? InScope(ShortcutProof? proof, string desktopPath) =>
        proof is not null && string.Equals(Path.GetDirectoryName(proof.Path), desktopPath, StringComparison.OrdinalIgnoreCase)
            ? proof : null;

    private static string PreferredName(string root, string displayName, string bindingId)
    {
        string suffix = " - 动效 [" + ShortcutReceiptStore.Key(bindingId)[..12] + "].lnk";
        int limit = Math.Min(50, 259 - root.Length - 1 - suffix.Length);
        if (limit < 2) throw new InvalidDataException("The desktop path is too long.");
        char[] invalid = Path.GetInvalidFileNameChars();
        string prefix = string.Concat(displayName.Select(c => invalid.Contains(c) ? '_' : c));
        if (prefix.Length > limit) prefix = prefix[..limit];
        if (prefix.Length > 0 && char.IsHighSurrogate(prefix[^1])) prefix = prefix[..^1];
        prefix = prefix.Trim(' ', '.');
        string name = prefix + suffix;
        return prefix.Length > 0 && LocalPathPolicy.IsUnambiguousName(name) ? name : "应用" + suffix;
    }

    private static ShortcutStatus Map(ShortcutReceiptStatus status) => status switch
    {
        ShortcutReceiptStatus.Busy => ShortcutStatus.Busy,
        ShortcutReceiptStatus.Unavailable => ShortcutStatus.Unavailable,
        _ => ShortcutStatus.StorageError
    };

    private static ShortcutStatus Map(RepositoryError error) =>
        error == RepositoryError.Busy ? ShortcutStatus.Busy : ShortcutStatus.StorageError;

    private enum Operation { Inspect, Create, Repair, Remove }
}
