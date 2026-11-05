using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using DesktopMotionStudio.Core.Instances;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Scenes;

internal static class F06Checks
{
    private const string SceneJson = """{"schemaVersion":1,"sceneId":"dragon-portal","sceneVersion":"1.0.0","name":"Dragon Portal","displayMode":"centered","assets":[]}""";

    public static int Run()
    {
        int failures = RunProtocolChecks();
        if (OperatingSystem.IsWindows())
        {
            failures += RunLifecycleChecks();
            failures += RunWindowsProcessIntegration();
        }
        else Console.WriteLine("SKIP F06 Windows-only lifecycle checks");
        return failures;
    }

    private static int RunProtocolChecks()
    {
        int failures = 0;
        Guid requestId = Guid.NewGuid();
        var ready = new MotionHostFrame(MotionHostFrame.CurrentProtocolVersion, requestId, MotionHostMessageKind.Ready);
        byte[] encoded = MotionHostFrameCodec.Encode(ready);
        MotionHostFrame decoded = MotionHostFrameCodec.Decode(encoded);
        Record("IPC frame round trips", decoded == ready, ref failures);
        using (var fragmentedStream = new ChunkedReadStream(encoded, 1))
        {
            MotionHostFrame? fragmented = MotionHostFrameCodec.ReadAsync(fragmentedStream).GetAwaiter().GetResult();
            Record("IPC reads partial headers and payloads exactly", fragmented == ready, ref failures);
        }
        Record("IPC rejects mismatched request ID",
            MotionHostFrameValidator.ValidateEnvelope(decoded, Guid.NewGuid()) == MotionHostError.RequestMismatch, ref failures);
        Record("IPC rejects unsupported protocol",
            MotionHostFrameValidator.ValidateEnvelope(decoded with { ProtocolVersion = 99 }, requestId) == MotionHostError.UnsupportedProtocol, ref failures);
        Record("IPC rejects invalid scene version",
            !new MotionHostStartRequest(Guid.NewGuid(), "dragon-portal", "01.0.0", SceneDisplayMode.Full).IsValid, ref failures);

        byte[] oversized = new byte[MotionHostFrameCodec.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(oversized, MotionHostFrameCodec.MaximumFrameBytes + 1);
        using var oversizedStream = new PrefixOnlyStream(oversized);
        bool rejectedBeforePayload = false;
        try { _ = MotionHostFrameCodec.ReadAsync(oversizedStream).GetAwaiter().GetResult(); }
        catch (InvalidDataException) { rejectedBeforePayload = oversizedStream.Position == MotionHostFrameCodec.HeaderSize; }
        Record("IPC rejects oversized frame before payload allocation", rejectedBeforePayload, ref failures);

        bool malformedRejected = false;
        try { _ = MotionHostFrameCodec.Decode(EncodeRawPayload(Encoding.UTF8.GetBytes("{"))); }
        catch (InvalidDataException) { malformedRejected = true; }
        Record("IPC rejects malformed JSON", malformedRejected, ref failures);

        bool unknownKindRejected = RejectRawFrame($"{{\"protocolVersion\":{MotionHostFrame.CurrentProtocolVersion},\"requestId\":\"{requestId:D}\",\"kind\":\"FutureKind\"}}");
        bool numericKindRejected = RejectRawFrame($"{{\"protocolVersion\":{MotionHostFrame.CurrentProtocolVersion},\"requestId\":\"{requestId:D}\",\"kind\":0}}");
        bool duplicatePropertyRejected = RejectRawFrame($"{{\"protocolVersion\":{MotionHostFrame.CurrentProtocolVersion},\"protocolVersion\":{MotionHostFrame.CurrentProtocolVersion},\"requestId\":\"{requestId:D}\",\"kind\":\"Ready\"}}");
        bool unknownFieldRejected = RejectRawFrame($"{{\"protocolVersion\":{MotionHostFrame.CurrentProtocolVersion},\"requestId\":\"{requestId:D}\",\"kind\":\"Ready\",\"unexpected\":true}}");
        Record("IPC rejects unknown and numeric message kinds", unknownKindRejected && numericKindRejected, ref failures);
        Record("IPC rejects duplicate and unknown frame properties", duplicatePropertyRejected && unknownFieldRejected, ref failures);

        var state = MotionHostProtocolState.AwaitingReady;
        bool invalidOrderRejected = !MotionHostFrameValidator.TryAdvance(
            state, MotionHostPeer.Launcher, MotionHostMessageKind.Start, out _);
        bool validSequence = MotionHostFrameValidator.TryAdvance(state, MotionHostPeer.Host,
                                MotionHostMessageKind.Ready, out state) &&
                            MotionHostFrameValidator.TryAdvance(state, MotionHostPeer.Launcher,
                                MotionHostMessageKind.Start, out state) &&
                            MotionHostFrameValidator.TryAdvance(state, MotionHostPeer.Host,
                                MotionHostMessageKind.Started, out state) && state == MotionHostProtocolState.Visible;
        Record("IPC state machine accepts handshake and rejects out-of-order start", validSequence && invalidOrderRejected, ref failures);

        var racingSkipState = MotionHostProtocolState.Visible;
        bool handoffSkipRaceAccepted = MotionHostFrameValidator.TryAdvance(racingSkipState,
                                           MotionHostPeer.Launcher, MotionHostMessageKind.Handoff, out racingSkipState) &&
                                       racingSkipState == MotionHostProtocolState.HandoffRequested &&
                                       MotionHostFrameValidator.TryAdvance(racingSkipState,
                                           MotionHostPeer.Host, MotionHostMessageKind.SkipRequested, out racingSkipState) &&
                                       racingSkipState == MotionHostProtocolState.Closing &&
                                       MotionHostFrameValidator.TryAdvance(racingSkipState,
                                           MotionHostPeer.Host, MotionHostMessageKind.Finished, out racingSkipState) &&
                                       racingSkipState == MotionHostProtocolState.Closed;
        Record("IPC preserves a real Skip input that races with handoff", handoffSkipRaceAccepted, ref failures);

        return failures;
    }

    private static int RunLifecycleChecks()
    {
        int failures = 0;
        string targetPath = Environment.ProcessPath ?? throw new InvalidOperationException("Current process path unavailable.");
        var target = new ExecutableTarget(targetPath, []);
        SceneManifest scene = SceneManifestReader.Read(Encoding.UTF8.GetBytes(SceneJson)).Manifest
            ?? throw new InvalidOperationException("Test scene did not parse.");

        var existingPlatform = new FakeWindowPlatform();
        LaunchTargetIdentity existingIdentity = ResolveIdentity(target);
        existingPlatform.AddProcess(new ProcessStamp(50_101, 100, 1, existingIdentity),
            new WindowCandidate((nint)50_201, new ProcessStamp(50_101, 100, 1, existingIdentity), true, false, false, false, false, false, true));
        int existingDispatches = 0;
        var existingFactory = new FakeMotionHostSessionFactory();
        var existingCoordinator = CreateLifecycle(existingPlatform, existingFactory,
            _ => { existingDispatches++; return new LaunchResult(LaunchStatus.Started, 1, ProcessCreationFileTimeUtc: 1); });
        MotionHostLifecycleResult existing = existingCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), target, scene).GetAwaiter().GetResult();
        Record("existing app activates without starting MotionHost or dispatching target",
            existing.TargetResult.Outcome == InstanceOutcome.ActivatedExisting && existing.HostOutcome == MotionHostOutcome.NotStarted &&
            existingFactory.StartCount == 0 && existingDispatches == 0, ref failures);

        var order = new List<string>();
        var newPlatform = new FakeWindowPlatform();
        var sessionFactory = new FakeMotionHostSessionFactory
        {
            StartHandler = _ => { order.Add("host"); return new FakeMotionHostSession(); }
        };
        var newCoordinator = CreateLifecycle(newPlatform, sessionFactory, resolved =>
        {
            order.Add("target");
            AddReadyProcess(newPlatform, resolved.Identity, 50_102, 101);
            return new LaunchResult(LaunchStatus.Started, 50_102, ProcessCreationFileTimeUtc: 101);
        });
        MotionHostLifecycleResult opened = newCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), target, scene).GetAwaiter().GetResult();
        Record("new launch starts host immediately before one target dispatch and hands off on ready",
            opened.TargetResult.Outcome == InstanceOutcome.WindowReady && opened.HostOutcome == MotionHostOutcome.HandedOff &&
            sessionFactory.StartCount == 1 && order.SequenceEqual(["host", "target"]), ref failures);

        var skipPlatform = new FakeWindowPlatform();
        var skipFactory = new FakeMotionHostSessionFactory
        {
            StartHandler = _ => new FakeMotionHostSession { HandoffOutcome = MotionHostOutcome.Skipped }
        };
        int skipDispatches = 0;
        var skipCoordinator = CreateLifecycle(skipPlatform, skipFactory, resolved =>
        {
            skipDispatches++;
            AddReadyProcess(skipPlatform, resolved.Identity, 50_103, 102);
            return new LaunchResult(LaunchStatus.Started, 50_103, ProcessCreationFileTimeUtc: 102);
        });
        MotionHostLifecycleResult skipped = skipCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), target, scene).GetAwaiter().GetResult();
        Record("visual skip does not cancel or repeat target launch", skipped.TargetResult.Outcome == InstanceOutcome.WindowReady &&
            skipped.HostOutcome == MotionHostOutcome.Skipped && skipDispatches == 1, ref failures);

        var failedFactory = new FakeMotionHostSessionFactory
        {
            StartHandler = _ => throw new IOException("simulated host process start failure")
        };
        var failOpenPlatform = new FakeWindowPlatform();
        int failOpenDispatches = 0;
        var failOpenCoordinator = CreateLifecycle(failOpenPlatform, failedFactory, resolved =>
        {
            failOpenDispatches++;
            AddReadyProcess(failOpenPlatform, resolved.Identity, 50_104, 103);
            return new LaunchResult(LaunchStatus.Started, 50_104, ProcessCreationFileTimeUtc: 103);
        });
        MotionHostLifecycleResult failOpen = failOpenCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), target, scene).GetAwaiter().GetResult();
        Record("host start failure is advisory and F04 still dispatches exactly once",
            failOpen.TargetResult.Outcome == InstanceOutcome.WindowReady && failOpen.TargetResult.LaunchPreparationErrorCode is not null &&
            failOpenDispatches == 1 && failOpen.HostOutcome == MotionHostOutcome.Failed, ref failures);

        var launchFailurePlatform = new FakeWindowPlatform();
        var launchFailureSession = new FakeMotionHostSession();
        var launchFailureFactory = new FakeMotionHostSessionFactory { StartHandler = _ => launchFailureSession };
        var launchFailureCoordinator = CreateLifecycle(launchFailurePlatform, launchFailureFactory,
            _ => new LaunchResult(LaunchStatus.StartFailed, NativeErrorCode: 5));
        MotionHostLifecycleResult launchFailure = launchFailureCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), target, scene).GetAwaiter().GetResult();
        Record("target dispatch failure closes only the host and preserves F05 outcome",
            launchFailure.TargetResult.Outcome == InstanceOutcome.LaunchFailed && launchFailure.HostOutcome == MotionHostOutcome.Exited &&
            launchFailureSession.ShutdownCount == 1, ref failures);

        var hostCrashPlatform = new FakeWindowPlatform();
        var hostCrashSession = new FakeMotionHostSession
        {
            HandoffOutcome = MotionHostOutcome.Failed,
            FailureReason = MotionHostError.RendererFailed
        };
        var hostCrashFactory = new FakeMotionHostSessionFactory { StartHandler = _ => hostCrashSession };
        var hostCrashCoordinator = CreateLifecycle(hostCrashPlatform, hostCrashFactory, resolved =>
        {
            AddReadyProcess(hostCrashPlatform, resolved.Identity, 50_105, 104);
            return new LaunchResult(LaunchStatus.Started, 50_105, ProcessCreationFileTimeUtc: 104);
        });
        MotionHostLifecycleResult hostCrash = hostCrashCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), target, scene).GetAwaiter().GetResult();
        Record("host crash leaves confirmed target result intact", hostCrash.TargetResult.Outcome == InstanceOutcome.WindowReady &&
            hostCrash.HostOutcome == MotionHostOutcome.Failed && hostCrash.HostError == MotionHostError.RendererFailed, ref failures);

        var throwingPlatform = new FakeWindowPlatform();
        var abandonedSession = new FakeMotionHostSession();
        var throwingFactory = new FakeMotionHostSessionFactory { StartHandler = _ => abandonedSession };
        var throwingCoordinator = CreateLifecycle(throwingPlatform, throwingFactory, _ => throw new InvalidOperationException("simulated F04 exception"));
        bool dispatchExceptionObserved = false;
        try { _ = throwingCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), target, scene).GetAwaiter().GetResult(); }
        catch (InvalidOperationException) { dispatchExceptionObserved = true; }
        Record("unexpected target dispatch exception still shuts down and disposes host",
            dispatchExceptionObserved && abandonedSession.ShutdownCount == 1 && abandonedSession.Disposed, ref failures);

        var invalidScenePlatform = new FakeWindowPlatform();
        var invalidSceneFactory = new FakeMotionHostSessionFactory();
        var invalidSceneCoordinator = CreateLifecycle(invalidScenePlatform, invalidSceneFactory, resolved =>
        {
            AddReadyProcess(invalidScenePlatform, resolved.Identity, 50_106, 105);
            return new LaunchResult(LaunchStatus.Started, 50_106, ProcessCreationFileTimeUtc: 105);
        });
        MotionHostLifecycleResult invalidScene = invalidSceneCoordinator.OpenAsync(Guid.NewGuid().ToString("N"),
            target, scene with { SceneVersion = "invalid" }).GetAwaiter().GetResult();
        Record("invalid scene fails open to one target dispatch without starting a host",
            invalidScene.TargetResult.Outcome == InstanceOutcome.WindowReady && invalidScene.HostOutcome == MotionHostOutcome.Failed &&
            invalidSceneFactory.StartCount == 0, ref failures);

        ScenePlaybackDefinition playback = ScenePlaybackReader.Read(Encoding.UTF8.GetBytes(F09Checks.PlaybackJson.Replace("studio-pulse", scene.SceneId)), scene).Value!;
        MotionHostStartRequest? captured = null;
        var playbackFactory = new FakeMotionHostSessionFactory { StartHandler = request => { captured = request; return new FakeMotionHostSession(); } };
        var playbackPlatform = new FakeWindowPlatform();
        int playbackDispatches = 0;
        var playbackCoordinator = CreateLifecycle(playbackPlatform, playbackFactory, resolved =>
        {
            playbackDispatches++; AddReadyProcess(playbackPlatform, resolved.Identity, 50_120, 120);
            return new LaunchResult(LaunchStatus.Started, 50_120, ProcessCreationFileTimeUtc: 120);
        });
        MotionHostLifecycleResult played = playbackCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), target, scene, playback: playback).GetAwaiter().GetResult();
        Record("shared playback launch carries only validated scene data and keeps ready-driven handoff", played.TargetResult.Outcome == InstanceOutcome.WindowReady && played.HostOutcome == MotionHostOutcome.HandedOff && playbackDispatches == 1 && captured is { Mode: MotionHostMode.Launch, Playback: not null } && captured.Playback.Keyframes.SequenceEqual(playback.Keyframes), ref failures);
        var invalidPlaybackFactory = new FakeMotionHostSessionFactory();
        var invalidPlaybackPlatform = new FakeWindowPlatform();
        int invalidPlaybackDispatches = 0;
        var invalidPlaybackCoordinator = CreateLifecycle(invalidPlaybackPlatform, invalidPlaybackFactory, resolved =>
        {
            invalidPlaybackDispatches++; AddReadyProcess(invalidPlaybackPlatform, resolved.Identity, 50_121, 121);
            return new LaunchResult(LaunchStatus.Started, 50_121, ProcessCreationFileTimeUtc: 121);
        });
        MotionHostLifecycleResult invalidPlayback = invalidPlaybackCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), target, scene, playback: playback with { SceneId = "wrong-scene" }).GetAwaiter().GetResult();
        Record("invalid playback snapshot fails open to exactly one target dispatch without host", invalidPlayback.TargetResult.Outcome == InstanceOutcome.WindowReady && invalidPlaybackDispatches == 1 && invalidPlaybackFactory.StartCount == 0 && invalidPlayback.HostError == MotionHostError.InvalidMessage, ref failures);
        return failures;
    }

    internal static int RunWindowsProcessIntegration()
    {
        string? hostPath = Environment.GetEnvironmentVariable("DMS_F06_HOST_PATH");
        if (string.IsNullOrWhiteSpace(hostPath) || !File.Exists(hostPath))
        {
            Console.WriteLine("SKIP MotionHost child-process integration (DMS_F06_HOST_PATH is unset or unavailable)");
            return 0;
        }

        int failures = 0;
        int existingHostProcesses = CountProcessesByName(Path.GetFileNameWithoutExtension(hostPath));
        HashSet<int> existingHostProcessIds = GetProcessIdsByName(Path.GetFileNameWithoutExtension(hostPath));
        var factory = new WindowsMotionHostProcessFactory(hostPath);
        int holdMs = int.TryParse(Environment.GetEnvironmentVariable("DMS_F06_HOST_WAIT_MS"), out int parsedHold)
            ? Math.Clamp(parsedHold, 0, 15_000)
            : 250;
        Console.WriteLine($"  real MotionHost visible hold requested: {holdMs}ms");
        var request = new MotionHostStartRequest(Guid.NewGuid(), "dragon-portal", "1.0.0", SceneDisplayMode.Centered);
        IMotionHostSession session = factory.Start(request);
        using Process? hostProcess = WaitForNewHostProcess(hostPath, existingHostProcessIds, TimeSpan.FromSeconds(2));
        nint hostWindowHandle = hostProcess is null ? 0 : WaitForMainWindow(hostProcess, TimeSpan.FromSeconds(2));
        bool hostWindowVisible = hostWindowHandle != 0 && NativeMotionHostMethods.IsWindowVisible(hostWindowHandle);
        if (hostProcess is not null)
            Console.WriteLine($"  real MotionHost window: PID {hostProcess.Id}, HWND {hostWindowHandle}, visible={hostWindowVisible}");
        Record("real MotionHost creates a visible desktop window", hostWindowVisible, ref failures);
        try
        {
            if (holdMs > 0) Thread.Sleep(holdMs);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            long handoffStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            MotionHostOutcome outcome = session.HandoffAsync(timeout.Token).GetAwaiter().GetResult();
            TimeSpan handoffElapsed = System.Diagnostics.Stopwatch.GetElapsedTime(handoffStartedAt);
            Console.WriteLine($"  real MotionHost lifecycle outcome: {outcome}");
            Console.WriteLine($"  real MotionHost handoff elapsed: {handoffElapsed.TotalMilliseconds:F0}ms");
            if (outcome == MotionHostOutcome.Failed)
                Console.WriteLine($"  MotionHost failure reason: {session.FailureReason?.ToString() ?? "unknown"}; native={session.NativeErrorCode?.ToString() ?? "none"}");
            Record("real MotionHost process completes same-user pipe handshake, skip, or handoff",
                outcome is MotionHostOutcome.HandedOff or MotionHostOutcome.Skipped, ref failures);
            Record("real MotionHost handoff fade completes within 300 ms when not skipped",
                outcome == MotionHostOutcome.Skipped || (outcome == MotionHostOutcome.HandedOff && handoffElapsed <= TimeSpan.FromMilliseconds(300)), ref failures);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL real MotionHost process integration: {ex.GetType().Name}: {ex.Message}");
            failures++;
        }
        finally
        {
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        bool hostExited = WaitForHostProcessCount(hostPath, existingHostProcesses, TimeSpan.FromSeconds(2));
        Record("real MotionHost child exits after handoff", hostExited, ref failures);

        MotionHostStartRequest timeoutRequest = request with { RequestId = Guid.NewGuid(), MaximumVisibleMilliseconds = 750 };
        IMotionHostSession timeoutSession = factory.Start(timeoutRequest);
        try
        {
            Thread.Sleep(2_000);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            MotionHostOutcome outcome = timeoutSession.HandoffAsync(timeout.Token).GetAwaiter().GetResult();
            if (outcome != MotionHostOutcome.TimedOut)
                Console.WriteLine($"  MotionHost deadline outcome: {outcome}; reason={timeoutSession.FailureReason?.ToString() ?? "none"}; native={timeoutSession.NativeErrorCode?.ToString() ?? "none"}");
            Record("real MotionHost watchdog closes the visible host on its monotonic deadline",
                outcome == MotionHostOutcome.TimedOut, ref failures);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL real MotionHost deadline integration: {ex.GetType().Name}: {ex.Message}");
            failures++;
        }
        finally { timeoutSession.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        Record("MotionHost watchdog child exits", WaitForHostProcessCount(hostPath, existingHostProcesses, TimeSpan.FromSeconds(2)), ref failures);

        int disconnectWaitMs = int.TryParse(Environment.GetEnvironmentVariable("DMS_F06_DISCONNECT_WAIT_MS"), out int parsedDisconnect)
            ? Math.Clamp(parsedDisconnect, 0, 10_000)
            : 750;
        ProcessStartInfo workerStart = CreateDisconnectWorkerStart(hostPath, disconnectWaitMs);
        workerStart.RedirectStandardOutput = true;
        workerStart.RedirectStandardError = true;
        Process? worker = null;
        bool workerExited = false;
        string workerDetails = "worker did not start";
        try
        {
            worker = Process.Start(workerStart);
            if (worker is not null)
            {
                Task<string> outputTask = worker.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = worker.StandardError.ReadToEndAsync();
                bool exited = worker.WaitForExit(15_000);
                if (!exited)
                {
                    try { worker.Kill(entireProcessTree: false); }
                    catch (InvalidOperationException) { }
                    worker.WaitForExit(1_000);
                }
                string output = outputTask.GetAwaiter().GetResult();
                string error = errorTask.GetAwaiter().GetResult();
                workerExited = exited && worker.ExitCode == 0;
                workerDetails = $"pid={worker.Id}, exited={exited}, code={(exited ? worker.ExitCode : -1)}, stdout={output}, stderr={error}";
            }
        }
        catch (Exception ex)
        {
            workerDetails = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally { worker?.Dispose(); }
        if (!workerExited) Console.WriteLine($"  parent-disconnect worker details: {workerDetails}");
        Record("parent-disconnect integration worker starts and exits", workerExited, ref failures);
        long disconnectCleanupStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        bool disconnectHostExited = WaitForHostProcessCount(hostPath, existingHostProcesses, TimeSpan.FromSeconds(1));
        TimeSpan disconnectCleanupElapsed = System.Diagnostics.Stopwatch.GetElapsedTime(disconnectCleanupStartedAt);
        bool disconnectClosedHost = workerExited && disconnectHostExited && disconnectCleanupElapsed <= TimeSpan.FromSeconds(1);
        Console.WriteLine($"  parent-disconnect host cleanup elapsed: {disconnectCleanupElapsed.TotalMilliseconds:F0}ms");
        Record("MotionHost closes within one second after abrupt parent process exit", disconnectClosedHost, ref failures);

        failures += RunRealTargetHostIntegration(hostPath);
        return failures;
    }

    private static int RunRealTargetHostIntegration(string hostPath)
    {
        string? publishedTarget = Environment.GetEnvironmentVariable("DMS_F06_TARGET_PATH");
        if (string.IsNullOrWhiteSpace(publishedTarget) || !File.Exists(publishedTarget))
        {
            Console.WriteLine("SKIP real target plus MotionHost integration (DMS_F06_TARGET_PATH is unset or unavailable)");
            return 0;
        }

        string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"dms-f06-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(publishedTarget)!))
            File.Copy(file, Path.Combine(temporaryDirectory, Path.GetFileName(file)));
        string targetPath = Path.Combine(temporaryDirectory, Path.GetFileName(publishedTarget));
        var platform = new WindowsWindowPlatform();
        var launcher = new ApplicationLauncher();
        var instances = new ApplicationInstanceCoordinator(platform, launcher.Launch);
        var coordinator = new MotionHostLifecycleCoordinator(instances, new WindowsMotionHostProcessFactory(hostPath));
        Process? targetProcess = null;
        nint? targetWindow = null;
        int failures = 0;
        try
        {
            var target = new ExecutableTarget(targetPath, [], temporaryDirectory);
            var scene = SceneManifestReader.Read(Encoding.UTF8.GetBytes(SceneJson)).Manifest!;
            MotionHostLifecycleResult result = coordinator.OpenAsync(Guid.NewGuid().ToString("N"), target, scene)
                .GetAwaiter().GetResult();
            targetWindow = result.TargetResult.WindowHandle;
            if (result.TargetResult.Process is ProcessStamp targetStamp && targetWindow is nint visibleTargetWindow)
                Console.WriteLine($"  real F06 target window: PID {targetStamp.ProcessId}, HWND {visibleTargetWindow}");
            if (result.TargetResult.Process is not null)
            {
                try { targetProcess = Process.GetProcessById(result.TargetResult.Process.ProcessId); }
                catch (ArgumentException) { }
            }

            bool readyAndHandedOff = result.TargetResult.Outcome == InstanceOutcome.WindowReady &&
                result.TargetResult.WindowHandle is not null && result.HostOutcome == MotionHostOutcome.HandedOff &&
                targetProcess is not null && !targetProcess.HasExited;
            Record("real target remains running through MotionHost handoff", readyAndHandedOff, ref failures);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL real target plus MotionHost integration: {ex.GetType().Name}: {ex.Message}");
            failures++;
        }
        finally
        {
            bool targetExited = targetProcess is null;
            if (targetProcess is not null)
            {
                try
                {
                    if (!targetProcess.HasExited && targetWindow is nint window && window != 0)
                        NativeTestMethods.PostMessage(window, 0x0010, 0, 0);
                    if (!targetProcess.HasExited) targetProcess.WaitForExit(3_000);
                    targetExited = targetProcess.HasExited;
                }
                catch (Exception) { }
                targetProcess.Dispose();
            }
            if (targetExited)
            {
                try { if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return failures;
    }

    private static ProcessStartInfo CreateDisconnectWorkerStart(string hostPath, int visibleDelayMs)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Check harness process path is unavailable.");
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(System.Reflection.Assembly.GetEntryAssembly()?.Location
                ?? throw new InvalidOperationException("Check harness assembly path is unavailable."));
        start.ArgumentList.Add("--f06-disconnect-worker");
        start.ArgumentList.Add(hostPath);
        start.ArgumentList.Add(visibleDelayMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return start;
    }

    private static bool WaitForHostProcessCount(string hostPath, int maximum, TimeSpan timeout)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        while (System.Diagnostics.Stopwatch.GetElapsedTime(start) < timeout)
        {
            Process[] processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(hostPath));
            foreach (Process process in processes) process.Dispose();
            if (processes.Length <= maximum) return true;
            Thread.Sleep(50);
        }
        return CountProcessesByName(Path.GetFileNameWithoutExtension(hostPath)) <= maximum;
    }

    private static int CountProcessesByName(string name)
    {
        Process[] processes = Process.GetProcessesByName(name);
        foreach (Process process in processes) process.Dispose();
        return processes.Length;
    }

    private static HashSet<int> GetProcessIdsByName(string name)
    {
        Process[] processes = Process.GetProcessesByName(name);
        try { return processes.Select(process => process.Id).ToHashSet(); }
        finally { foreach (Process process in processes) process.Dispose(); }
    }

    private static Process? WaitForNewHostProcess(string hostPath, IReadOnlySet<int> existingProcessIds, TimeSpan timeout)
    {
        long startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        string name = Path.GetFileNameWithoutExtension(hostPath);
        while (System.Diagnostics.Stopwatch.GetElapsedTime(startedAt) < timeout)
        {
            Process[] processes = Process.GetProcessesByName(name);
            Process? match = processes.FirstOrDefault(process => !existingProcessIds.Contains(process.Id));
            foreach (Process process in processes)
                if (!ReferenceEquals(process, match)) process.Dispose();
            if (match is not null) return match;
            Thread.Sleep(25);
        }
        return null;
    }

    private static nint WaitForMainWindow(Process process, TimeSpan timeout)
    {
        long startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        while (System.Diagnostics.Stopwatch.GetElapsedTime(startedAt) < timeout)
        {
            try
            {
                process.Refresh();
                if (process.HasExited) return 0;
                nint handle = NativeMotionHostMethods.FindVisibleTopLevelWindow(process.Id);
                if (handle != 0 && NativeMotionHostMethods.IsWindowVisible(handle)) return handle;
            }
            catch (InvalidOperationException) { return 0; }
            Thread.Sleep(25);
        }
        return 0;
    }

    private static MotionHostLifecycleCoordinator CreateLifecycle(
        FakeWindowPlatform platform,
        IMotionHostSessionFactory factory,
        Func<ResolvedLaunchTarget, LaunchResult> launch)
    {
        var instances = new ApplicationInstanceCoordinator(platform, launch, windowWait: TimeSpan.FromSeconds(1));
        return new MotionHostLifecycleCoordinator(instances, factory);
    }

    private static LaunchTargetIdentity ResolveIdentity(LaunchTarget target) =>
        new LaunchTargetIdentityResolver().Resolve(target).Target?.Identity
        ?? throw new InvalidOperationException("Test launch identity did not resolve.");

    private static void AddReadyProcess(FakeWindowPlatform platform, LaunchTargetIdentity identity, int processId, long creationTime)
    {
        var process = new ProcessStamp(processId, creationTime, platform.CurrentSessionId, identity);
        platform.AddProcess(process, new WindowCandidate((nint)(processId + 100), process,
            true, false, false, false, false, false, true));
    }

    private static byte[] EncodeRawPayload(byte[] payload)
    {
        byte[] frame = new byte[MotionHostFrameCodec.HeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, checked((uint)payload.Length));
        payload.CopyTo(frame.AsSpan(MotionHostFrameCodec.HeaderSize));
        return frame;
    }

    private static void Record(string name, bool passed, ref int failures)
    {
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}");
        if (!passed) failures++;
    }

    private sealed class PrefixOnlyStream(byte[] prefix) : MemoryStream(prefix) { }

    private static bool RejectRawFrame(string json)
    {
        try { _ = MotionHostFrameCodec.Decode(EncodeRawPayload(Encoding.UTF8.GetBytes(json))); }
        catch (InvalidDataException) { return true; }
        return false;
    }

    private sealed class ChunkedReadStream(byte[] bytes, int maxReadBytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, maxReadBytes)], cancellationToken);
    }

    private static class NativeMotionHostMethods
    {
        private delegate bool EnumWindowCallback(nint windowHandle, nint parameter);

        internal static nint FindVisibleTopLevelWindow(int processId)
        {
            nint found = 0;
            EnumWindowCallback callback = (windowHandle, parameter) =>
            {
                if (!IsWindowVisible(windowHandle)) return true;
                _ = GetWindowThreadProcessId(windowHandle, out uint ownerProcessId);
                if (ownerProcessId != (uint)processId) return true;
                found = windowHandle;
                return false;
            };
            _ = EnumWindows(callback, 0);
            return found;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowCallback callback, nint parameter);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(nint windowHandle);
    }
}
