using DesktopMotionStudio.Core.Launch;

namespace DesktopMotionStudio.Core.Instances;

public interface IWindowPlatform
{
    uint CurrentSessionId { get; }
    ProcessScanResult FindMatchingProcesses(LaunchTargetIdentity identity);
    ProcessVerificationResult VerifyProcess(int processId, LaunchTargetIdentity identity, uint expectedSessionId, long? expectedCreationFileTimeUtc = null);
    WindowScanResult FindWindows(ProcessStamp process);
    WindowActivationResult Activate(WindowCandidate candidate);
}
