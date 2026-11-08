using System.Diagnostics;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.MotionHost;
using System.IO.Pipes;
using System.Runtime.InteropServices;

internal static class F09Checks
{
    internal const string ManifestJson = """{"schemaVersion":1,"sceneId":"studio-pulse","sceneVersion":"1.0.0","name":"Studio Pulse","displayMode":"centered","assets":[]}""";
    internal const string PresentationJson = """{"schemaVersion":1,"sceneId":"studio-pulse","sceneVersion":"1.0.0","description":"Original development work","style":"Orbit","theme":"Neutral","cover":"cover.png"}""";
    internal const string PlaybackJson = """{"schemaVersion":1,"sceneId":"studio-pulse","sceneVersion":"1.0.0","runtimeId":"vector-orbit","runtimeVersion":1,"durationMilliseconds":3000,"accent":"#618EFF","glyph":"M","keyframes":[{"atMilliseconds":0,"scale":0.55,"opacity":0,"rotationDegrees":-45},{"atMilliseconds":1500,"scale":1.15,"opacity":1,"rotationDegrees":180},{"atMilliseconds":3000,"scale":0.85,"opacity":0,"rotationDegrees":360}]}""";

    internal static int Run()
    {
        int failures = 0;
        string root = Path.Combine(Path.GetTempPath(), $"dms-f09-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try { Readers(ref failures); FilesAndLibrary(root, ref failures); Timeline(ref failures); PlaybackProtocol(ref failures); }
        catch (Exception ex) { Console.WriteLine($"FAIL F09 checks: {ex.GetType().Name}: {ex.Message}"); failures++; }
        finally { Directory.Delete(root, recursive: true); }
        return failures + F09BindingChecks.Run();
    }

    internal static int VerifyShippedCatalog(string root, bool requireTech = false)
    {
        int failures = 0;
        SceneLibrarySnapshot result = new SceneLibrary().Scan(root);
        SceneLibraryItem? work = result.Items.SingleOrDefault(item => item.Catalog.Manifest?.SceneId == "studio-pulse");
        Check("F09 shipped development data has validated identity and required playback", result.Error == SceneLibraryError.None && work is { IsPlayable: true, Playback.RuntimeId: "vector-orbit", Playback.DurationMilliseconds: 5000 } && work.Catalog.Manifest!.SceneVersion == "0.1.0", ref failures);
        Check("F09 shipped work is labelled as development content", work?.Presentation?.Theme.Contains("开发", StringComparison.Ordinal) == true && work.Catalog.Manifest!.Name.Contains("开发", StringComparison.Ordinal), ref failures);
        if (requireTech)
        {
            SceneLibraryItem? tech = result.Items.SingleOrDefault(item => item.Catalog.Manifest?.SceneId == "zero-boundary");
            Check("F13 shipped catalog includes playable 零界, cover, and attribution",
                tech is { IsPlayable: true, DisplayName: "零界", Playback.IsTechFrame: true, Playback.DurationMilliseconds: 2000,
                    CoverError: SceneAssetError.None } &&
                tech.Catalog.Manifest!.DisplayMode == SceneDisplayMode.Centered &&
                File.Exists(Path.Combine(tech.Catalog.DirectoryPath, "ATTRIBUTION.md")), ref failures);
        }
        return failures;
    }

    internal static int StaleHostWorker(string pipeName, string requestText)
    {
        if (!Guid.TryParseExact(requestText, "D", out Guid request) || request == Guid.Empty || pipeName != $"dms-motionhost-{request:N}") return 2;
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            pipe.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            pipe.Write(RawFrame($"{{\"protocolVersion\":1,\"requestId\":\"{request:D}\",\"kind\":\"Ready\"}}"));
            pipe.Flush();
            _ = pipe.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception) { return 3; }
    }

    private static void Readers(ref int failures)
    {
        SceneManifest manifest = SceneManifestReader.Read(Bytes(ManifestJson)).Manifest!;
        var presentation = ScenePresentationReader.Read(Bytes(PresentationJson), manifest);
        var playback = ScenePlaybackReader.Read(Bytes(PlaybackJson), manifest);
        Check("F09 sidecars retain separate versions and matching scene identity", presentation.Value?.Theme == "Neutral" && playback.Value is { IsValid: true, RuntimeId: "vector-orbit" }, ref failures);
        string tech = PlaybackJson.Replace("vector-orbit", "tech-frame").Replace("3000", "2000");
        Check("F13 tech-frame v1 opens without changing the legacy vector definition",
            ScenePlaybackReader.Read(Bytes(tech), manifest).Value is { IsValid: true, RuntimeId: "tech-frame" } &&
            playback.Value is { IsValid: true, RuntimeId: "vector-orbit" }, ref failures);
        Check("F13 unknown tech-frame versions are rejected",
            ScenePlaybackReader.Read(Bytes(tech.Replace("\"runtimeVersion\":1", "\"runtimeVersion\":2")), manifest).Error == SceneDataError.UnsupportedRuntime,
            ref failures);
        Check("F13 finite tech-frame budget rejects longer content",
            ScenePlaybackReader.Read(Bytes(tech.Replace("2000", "2500")), manifest).Error == SceneDataError.InvalidDefinition,
            ref failures);
        foreach ((string name, string json, SceneDataError error) in new[]
        {
            ("future before unknown shape", "{\"schemaVersion\":2,\"unknown\":{}}", SceneDataError.UnsupportedSchema),
            ("non-number schema", PlaybackJson.Replace("\"schemaVersion\":1", "\"schemaVersion\":\"1\""), SceneDataError.InvalidSchema),
            ("missing schema", PlaybackJson.Replace("\"schemaVersion\":1,", ""), SceneDataError.InvalidSchema),
            ("duplicate root", PlaybackJson.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"), SceneDataError.InvalidJson),
            ("duplicate nested", PlaybackJson.Replace("\"scale\":0.55", "\"scale\":0.55,\"scale\":0.55"), SceneDataError.InvalidJson),
            ("unknown member", PlaybackJson.Insert(1, "\"shell\":\"ignored.exe\","), SceneDataError.InvalidJson),
            ("ignored computed member", PlaybackJson.Insert(1, "\"isValid\":false,"), SceneDataError.InvalidJson),
            ("missing required frame", PlaybackJson.Replace("\"opacity\":0,", ""), SceneDataError.InvalidJson),
            ("unknown runtime", PlaybackJson.Replace("vector-orbit", "arbitrary-module"), SceneDataError.UnsupportedRuntime),
            ("new runtime version", PlaybackJson.Replace("\"runtimeVersion\":1", "\"runtimeVersion\":2"), SceneDataError.UnsupportedRuntime),
            ("identity mismatch", PlaybackJson.Replace("studio-pulse", "different-scene"), SceneDataError.IdentityMismatch),
            ("version mismatch", PlaybackJson.Replace("1.0.0", "1.0.1"), SceneDataError.IdentityMismatch)
        }) Check($"F09 playback rejects {name}", ScenePlaybackReader.Read(Bytes(json), manifest).Error == error, ref failures);

        foreach ((string name, Action<JsonObject> mutate) in new (string, Action<JsonObject>)[]
        {
            ("duration below minimum", node => node["durationMilliseconds"] = 999),
            ("duration over limit", node => node["durationMilliseconds"] = 8001),
            ("nonzero first time", node => node["keyframes"]![0]!["atMilliseconds"] = 1),
            ("missing endpoint", node => node["keyframes"]![2]!["atMilliseconds"] = 2999),
            ("nonincreasing times", node => node["keyframes"]![1]!["atMilliseconds"] = 0),
            ("scale over limit", node => node["keyframes"]![1]!["scale"] = 1.5001),
            ("negative opacity", node => node["keyframes"]![0]!["opacity"] = -0.1),
            ("rotation over limit", node => node["keyframes"]![2]!["rotationDegrees"] = 361),
            ("empty frames", node => node["keyframes"] = new JsonArray()),
            ("too many frames", node => node["keyframes"] = new JsonArray(Enumerable.Range(0, 9).Select(index => JsonNode.Parse($$"""{"atMilliseconds":{{index * 375}},"scale":1,"opacity":1,"rotationDegrees":0}""")).ToArray())),
            ("invalid accent", node => node["accent"] = "red"),
            ("overlong glyph", node => node["glyph"] = "123456789"),
            ("invisible glyph", node => node["glyph"] = "\u200B"),
            ("combining-only glyph", node => node["glyph"] = "\u034F"),
            ("selector-only glyph", node => node["glyph"] = "\uFE0F")
        })
        {
            JsonObject node = JsonNode.Parse(PlaybackJson)!.AsObject(); mutate(node);
            Check($"F09 playback rejects {name}", ScenePlaybackReader.Read(Bytes(node.ToJsonString()), manifest).Error == SceneDataError.InvalidDefinition, ref failures);
        }
        Check("F09 reader rejects non-finite JSON numbers", ScenePlaybackReader.Read(Bytes(PlaybackJson.Replace("0.55", "1e999")), manifest).Error is SceneDataError.InvalidJson or SceneDataError.InvalidDefinition, ref failures);
        byte[] limit = Enumerable.Repeat((byte)' ', ScenePlaybackReader.MaximumBytes).ToArray(); Bytes(PlaybackJson).CopyTo(limit, 0);
        Check("F09 exact sidecar byte limit accepts compact valid snapshot", ScenePlaybackReader.Read(limit, manifest).Value is { IsValid: true }, ref failures);
        Check("F09 sidecar byte overflow rejects before parsing", ScenePlaybackReader.Read(new byte[limit.Length + 1], manifest).Error == SceneDataError.TooLarge, ref failures);
        JsonObject maximum = JsonNode.Parse(PlaybackJson)!.AsObject();
        maximum["keyframes"] = new JsonArray(Enumerable.Range(0, 8).Select(index => JsonNode.Parse($$"""{"atMilliseconds":{{index * 3000 / 7}},"scale":1,"opacity":1,"rotationDegrees":0}""")).ToArray());
        Check("F09 exactly eight keyframes remain supported", ScenePlaybackReader.Read(Bytes(maximum.ToJsonString()), manifest).Value is { IsValid: true, Keyframes.Length: 8 }, ref failures);
        ScenePlaybackDefinition valid = playback.Value!;
        Check("F09 directly constructed nonfinite or invalid Unicode snapshots reject", !(valid with { Glyph = "\uD800" }).IsValid && !(valid with { Keyframes = valid.Keyframes.SetItem(0, valid.Keyframes[0] with { Scale = double.NaN }) }).IsValid, ref failures);
        Check("F09 visible base with combining mark remains allowed", (valid with { Glyph = "A\u0301" }).IsValid, ref failures);
        Check("F09 optional presentation rejects future version independently", ScenePresentationReader.Read(Bytes("{\"schemaVersion\":2,\"unknown\":0}"), manifest).Error == SceneDataError.UnsupportedSchema, ref failures);
        Check("F09 presentation rejects identity mismatch", ScenePresentationReader.Read(Bytes(PresentationJson.Replace("studio-pulse", "different-scene")), manifest).Error == SceneDataError.IdentityMismatch, ref failures);
        Check("F09 presentation rejects unknown member", ScenePresentationReader.Read(Bytes(PresentationJson.Insert(1, "\"unknown\":0,")), manifest).Error == SceneDataError.InvalidJson, ref failures);
        Check("F09 presentation rejects controls and text overflow", ScenePresentationReader.Read(Bytes(PresentationJson.Replace("Neutral", "Bad\\u001b")), manifest).Error == SceneDataError.InvalidDefinition && ScenePresentationReader.Read(Bytes(PresentationJson.Replace("Neutral", new string('x', 121))), manifest).Error == SceneDataError.InvalidDefinition, ref failures);
        foreach (string path in new[] { "../cover.png", "/cover.png", "C:/cover.png", "https://invalid/cover.png", "art\\cover.png", "art/./cover.png", "art//cover.png", "cover.png:stream", "art./cover.png", "NUL.png", "COM¹.png", " NUL.png" })
        {
            JsonObject node = JsonNode.Parse(PresentationJson)!.AsObject(); node["cover"] = path;
            Check($"F09 rejects unsafe cover reference {path}", ScenePresentationReader.Read(Bytes(node.ToJsonString()), manifest).Error == SceneDataError.UnsafeAssetPath, ref failures);
        }
        Check("F09 absolute and network roots reject safely", SceneAssetResolver.ReadFile("\\\\invalid.example\\share", "cover.png", 100).Error == SceneAssetError.InvalidPath && SceneAssetResolver.ReadFile("relative", "cover.png", 100).Error == SceneAssetError.InvalidPath, ref failures);
    }

    private static void FilesAndLibrary(string root, ref int failures)
    {
        string catalog = Path.Combine(root, "catalog"), scene = WriteScene(catalog, "one");
        string guard = Path.Combine(root, "profile.json"); File.WriteAllText(guard, "unchanged"); File.WriteAllText(guard + ".bak", "previous");
        var library = new SceneLibrary();
        SceneLibraryItem item = library.Scan(catalog).Items.Single();
        Check("F09 missing optional cover keeps playback available", item.IsPlayable && item.CoverError == SceneAssetError.Missing && item.DisplayName == "Studio Pulse", ref failures);
        Check("F09 constructed unsupported item cannot claim playable", !(item with { Playback = item.Playback! with { RuntimeVersion = 999 } }).IsPlayable, ref failures);
        using (var cover = new FileStream(Path.Combine(scene, "cover.png"), FileMode.Create, FileAccess.Write)) cover.SetLength(SceneLibrary.MaximumCoverBytes + 1L);
        item = library.Scan(catalog).Items.Single();
        Check("F09 oversized optional cover falls back without disabling playback", item.IsPlayable && item.CoverError == SceneAssetError.TooLarge, ref failures);
        File.Delete(Path.Combine(scene, "cover.png"));
        File.Delete(Path.Combine(scene, "scene.presentation.json"));
        item = library.Scan(catalog).Items.Single();
        Check("F09 missing presentation uses core name without disabling playback", item.IsPlayable && item.Presentation is null && item.PresentationError == SceneDataError.Missing, ref failures);
        File.WriteAllText(Path.Combine(scene, "scene.presentation.json"), "{\"schemaVersion\":2,\"unknown\":0}");
        item = library.Scan(catalog).Items.Single();
        Check("F09 future presentation falls back with separate diagnostic", item.IsPlayable && item.PresentationError == SceneDataError.UnsupportedSchema, ref failures);
        File.WriteAllText(Path.Combine(scene, "scene.playback.json"), PlaybackJson.Replace("vector-orbit", "unsupported"));
        item = library.Scan(catalog).Items.Single();
        Check("F09 integrity alone does not authorize unsupported playback", item.Catalog.IsAvailable && !item.IsPlayable && item.Playback is null && item.PlaybackError == SceneDataError.UnsupportedRuntime, ref failures);
        File.Delete(Path.Combine(scene, "scene.playback.json"));
        Check("F09 legacy manifest remains visible but cannot fake a preview", library.Scan(catalog).Items.Single() is { IsPlayable: false, PlaybackError: SceneDataError.Missing }, ref failures);
        WriteScene(catalog, "two", "second-work");
        Check("F09 one unavailable work does not block the other", library.Scan(catalog).Items.Count(item => item.IsPlayable) == 1, ref failures);
        WriteScene(catalog, "duplicate", "second-work");
        Check("F09 all duplicate IDs are unavailable", library.Scan(catalog).Items.Where(item => item.Catalog.Manifest?.SceneId == "second-work").All(item => !item.IsPlayable && item.Catalog.Diagnostic == SceneCatalogDiagnostic.DuplicateSceneId), ref failures);
        Check("F09 scanning never writes profile or backup", File.ReadAllText(guard) == "unchanged" && File.ReadAllText(guard + ".bak") == "previous", ref failures);
        Check("F09 absent catalog reports unavailable rather than empty success", library.Scan(Path.Combine(root, "absent")).Error == SceneLibraryError.Unavailable, ref failures);

        string bytesFile = Path.Combine(scene, "bytes.bin"); File.WriteAllBytes(bytesFile, new byte[100]);
        Check("F09 resource byte bound is exact", SceneAssetResolver.ReadFile(scene, "bytes.bin", 100).Bytes?.Length == 100 && SceneAssetResolver.ReadFile(scene, "bytes.bin", 99).Error == SceneAssetError.TooLarge, ref failures);
        using (var locked = new FileStream(bytesFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check("F09 locked content returns a recoverable I/O error", SceneAssetResolver.ReadFile(scene, "bytes.bin", 100).Error == SceneAssetError.IoError, ref failures);
        Check("F09 directory cannot masquerade as file", SceneAssetResolver.ReadFile(catalog, "one", 100).Error == SceneAssetError.NotAFile, ref failures);

        string cap = Path.Combine(root, "capacity"); WriteScene(cap, "000");
        for (int i = 1; i < 256; i++) Directory.CreateDirectory(Path.Combine(cap, i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)));
        Check("F09 exactly 256 directories remain enumerable", library.Scan(cap) is { Error: SceneLibraryError.None, Items.Count: 256 }, ref failures);
        WriteScene(cap, "256");
        int sidecarReads = 0;
        var observed = new SceneLibrary((directory, relative, max) => { sidecarReads++; return SceneAssetResolver.ReadFile(directory, relative, max); });
        SceneLibrarySnapshot overflow = observed.Scan(cap);
        Check("F09 directory 257 fails closed before sidecar reads and hidden duplicates", overflow.Error == SceneLibraryError.TooManyScenes && overflow.Items.Count == 0 && sidecarReads == 0, ref failures);
        if (OperatingSystem.IsWindows()) Junctions(root, ref failures);
    }

    private static void Junctions(string root, ref int failures)
    {
        string catalog = Path.Combine(root, "junction-catalog"), scene = WriteScene(catalog, "one");
        string alias = Path.Combine(root, "catalog-alias"), outside = Path.Combine(root, "outside"), art = Path.Combine(scene, "art");
        Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "cover.png"), "not to be followed");
        CreateJunction(alias, catalog);
        try
        {
            Check("F09 root ancestry junction rejects before catalog/asset reads", !new BuiltInSceneCatalog().Scan(alias).RootAvailable && !new BuiltInSceneCatalog().Scan(Path.Combine(alias, "one")).RootAvailable && new SceneLibrary().Scan(alias).Error == SceneLibraryError.Unavailable && SceneAssetResolver.ReadFile(Path.Combine(alias, "one"), "scene.json", 1000).Error == SceneAssetError.ReparsePoint, ref failures);
            CreateJunction(art, outside);
            try
            {
                File.WriteAllText(Path.Combine(scene, "scene.presentation.json"), PresentationJson.Replace("cover.png", "art/cover.png"));
                SceneLibraryItem item = new SceneLibrary().Scan(catalog).Items.Single();
                Check("F09 cover junction is rejected without disabling the work", item.IsPlayable && item.CoverError == SceneAssetError.ReparsePoint, ref failures);
                File.WriteAllText(Path.Combine(scene, "scene.json"), ManifestJson.Replace("\"assets\":[]", "\"assets\":[\"art/cover.png\"]"));
                Check("F09 required reparse asset disables catalog item", new BuiltInSceneCatalog().Scan(catalog).Items.Single().Diagnostic == SceneCatalogDiagnostic.ReparsePoint, ref failures);
            }
            finally { Directory.Delete(art, recursive: false); }
        }
        finally { Directory.Delete(alias, recursive: false); }
    }

    private static void Timeline(ref int failures)
    {
        SceneManifest manifest = SceneManifestReader.Read(Bytes(ManifestJson)).Manifest!;
        ScenePlaybackDefinition definition = ScenePlaybackReader.Read(Bytes(PlaybackJson), manifest).Value!;
        var timeline = new SceneTimeline(definition);
        SceneFrame start = timeline.Evaluate(TimeSpan.Zero);
        SceneFrame end = timeline.Evaluate(TimeSpan.FromMilliseconds(3000));
        Check("F09 timeline clamps time and preserves exact authored endpoints", start is { Scale: .55, Opacity: 0, RotationDegrees: -45, Completed: false } && end is { Scale: .85, Opacity: 0, RotationDegrees: 360, Completed: true } && timeline.Evaluate(TimeSpan.MinValue) == start && timeline.Evaluate(TimeSpan.MaxValue) == end, ref failures);
        SceneFrame midpoint = timeline.Evaluate(TimeSpan.FromMilliseconds(750));
        Check("F09 timeline interpolates every authored channel", Math.Abs(midpoint.Scale - .85) < 1e-12 && midpoint.Opacity == .5 && midpoint.RotationDegrees == 67.5, ref failures);
        SceneFrame quarter = timeline.Evaluate(TimeSpan.FromMilliseconds(375));
        Check("F09 smoothstep uses the authored easing away from midpoint", Math.Abs(quarter.Scale - .64375) < 1e-12 && quarter.Opacity == .15625 && quarter.RotationDegrees == -9.84375, ref failures);
        Check("F09 authored interior keyframe remains exact", timeline.Evaluate(TimeSpan.FromMilliseconds(1500)) is { Scale: 1.15, Opacity: 1, RotationDegrees: 180, Completed: false }, ref failures);
        SceneFrame reduced = timeline.Evaluate(TimeSpan.Zero, reducedMotion: true);
        Check("F09 reduced path holds still geometry and retains finite completion", reduced is { Scale: 1, Opacity: 1, RotationDegrees: 0, Completed: false } && timeline.Evaluate(TimeSpan.FromMilliseconds(2999), true) == reduced && timeline.Evaluate(TimeSpan.FromMilliseconds(3000), true).Completed, ref failures);
        Check("F09 elapsed time rather than tick history controls completion", Enumerable.Range(0, 1000).All(_ => !timeline.Evaluate(TimeSpan.FromMilliseconds(100)).Completed) && timeline.Evaluate(TimeSpan.FromSeconds(3)).Completed, ref failures);
        Check("F09 pure timeline gives deterministic concurrent frames", Enumerable.Range(0, 20).AsParallel().Select(_ => timeline.Evaluate(TimeSpan.FromMilliseconds(2250))).Distinct().Count() == 1, ref failures);
        bool rejected = false;
        try { _ = new SceneTimeline(definition with { Keyframes = definition.Keyframes.SetItem(0, definition.Keyframes[0] with { Scale = double.PositiveInfinity }) }); }
        catch (ArgumentException) { rejected = true; }
        Check("F09 timeline rejects invalid snapshots before execution", rejected, ref failures);
    }

    private static void PlaybackProtocol(ref int failures)
    {
        SceneManifest manifest = SceneManifestReader.Read(Bytes(ManifestJson)).Manifest!;
        ScenePlaybackDefinition definition = ScenePlaybackReader.Read(Bytes(PlaybackJson), manifest).Value!;
        Guid id = Guid.NewGuid();
        var request = new MotionHostStartRequest(id, manifest.SceneId, manifest.SceneVersion, manifest.DisplayMode, 10_000, MotionHostMode.Preview, definition);
        MotionHostFrame frame = request.ToStartFrame();
        MotionHostFrame decoded = MotionHostFrameCodec.Decode(MotionHostFrameCodec.Encode(frame));
        Check("F09 IPC v2 roundtrips explicit preview and immutable playback", MotionHostFrame.CurrentProtocolVersion == 2 && request.IsValid && decoded.Start is { Mode: MotionHostMode.Preview, Playback.IsValid: true } && decoded.Start.Playback.Keyframes.SequenceEqual(definition.Keyframes), ref failures);
        Check("F09 preview requires playback, matching identity and watchdog headroom", !(request with { Playback = null }).IsValid && !(request with { SceneId = "wrong-scene" }).IsValid && !(request with { MaximumVisibleMilliseconds = 3000 }).IsValid, ref failures);
        Check("F09 lifecycle-only diagnostic launch stays valid", (request with { Mode = MotionHostMode.Launch, Playback = null }).IsValid, ref failures);
        string json = Encoding.UTF8.GetString(MotionHostFrameCodec.Encode(frame).AsSpan(MotionHostFrameCodec.HeaderSize));
        foreach ((string name, Action<JsonObject> mutate) in new (string, Action<JsonObject>)[]
        {
            ("missing kind", node => node.Remove("kind")),
            ("missing mode", node => node["start"]!.AsObject().Remove("mode")),
            ("numeric mode", node => node["start"]!["mode"] = 1),
            ("unknown mode", node => node["start"]!["mode"] = "Future"),
            ("target authority", node => node["start"]!["appId"] = "not-allowed"),
            ("ignored start property", node => node["start"]!["isValid"] = true),
            ("ignored snapshot property", node => node["start"]!["playback"]!["isValid"] = true),
            ("unknown keyframe property", node => node["start"]!["playback"]!["keyframes"]![0]!["eval"] = "script")
            ,("invalid snapshot channels", node => node["start"]!["playback"]!["keyframes"]![0]!["opacity"] = -1)
            ,("missing preview playback", node => node["start"]!["playback"] = null)
            ,("inconsistent scene identity", node => node["start"]!["playback"]!["sceneId"] = "other-scene")
        })
        {
            JsonObject node = JsonNode.Parse(json)!.AsObject(); mutate(node);
            bool rejected = false;
            try { _ = MotionHostFrameCodec.Decode(RawFrame(node.ToJsonString())); }
            catch (InvalidDataException) { rejected = true; }
            Check($"F09 IPC rejects {name}", rejected, ref failures);
        }
        foreach (int version in new[] { 1, 3 })
        {
            bool rejected = false;
            try { _ = MotionHostFrameCodec.Decode(RawFrame($"{{\"protocolVersion\":{version},\"unknownFutureShape\":{{}}}}")); }
            catch (MotionHostProtocolException ex) { rejected = ex.Error == MotionHostError.UnsupportedProtocol; }
            Check($"F09 IPC rejects protocol {version} before typed future shape", rejected, ref failures);
        }
        foreach (string field in new[] { "schemaVersion", "runtimeVersion" })
        {
            JsonObject node = JsonNode.Parse(json)!.AsObject();
            node["start"]!["playback"] = new JsonObject { [field] = 99, ["unknownFutureShape"] = new JsonObject() };
            bool rejected = false;
            try { _ = MotionHostFrameCodec.Decode(RawFrame(node.ToJsonString())); }
            catch (MotionHostProtocolException ex) { rejected = ex.Error == MotionHostError.UnsupportedPlayback; }
            Check($"F09 IPC protects independent future playback {field}", rejected, ref failures);
        }
        Check("F09 IPC preview completion is mode-specific and handles stop race",
            MotionHostFrameValidator.TryAdvance(MotionHostProtocolState.Visible, MotionHostPeer.Host, MotionHostMessageKind.Completed, out var completed, MotionHostMode.Preview) && completed == MotionHostProtocolState.Closed &&
            MotionHostFrameValidator.TryAdvance(MotionHostProtocolState.Closing, MotionHostPeer.Host, MotionHostMessageKind.Completed, out _, MotionHostMode.Preview) &&
            !MotionHostFrameValidator.TryAdvance(MotionHostProtocolState.Visible, MotionHostPeer.Host, MotionHostMessageKind.Completed, out _, MotionHostMode.Launch) &&
            !MotionHostFrameValidator.TryAdvance(MotionHostProtocolState.Visible, MotionHostPeer.Launcher, MotionHostMessageKind.Handoff, out _, MotionHostMode.Preview), ref failures);
        Check("F09 IPC completion rejects premature or parent-authored events",
            !MotionHostFrameValidator.TryAdvance(MotionHostProtocolState.Starting, MotionHostPeer.Host, MotionHostMessageKind.Completed, out _, MotionHostMode.Preview) &&
            !MotionHostFrameValidator.TryAdvance(MotionHostProtocolState.Visible, MotionHostPeer.Launcher, MotionHostMessageKind.Completed, out _, MotionHostMode.Preview), ref failures);
        bool oversize = false;
        try { _ = MotionHostFrameCodec.Decode(new byte[MotionHostFrameCodec.MaximumFrameBytes + MotionHostFrameCodec.HeaderSize + 1]); }
        catch (InvalidDataException) { oversize = true; }
        Check("F09 IPC retains frame bound and request correlation", oversize && MotionHostFrameValidator.ValidateEnvelope(frame, Guid.NewGuid()) == MotionHostError.RequestMismatch, ref failures);
        if (OperatingSystem.IsWindows())
        {
            string harness = Path.Combine(AppContext.BaseDirectory, "DesktopMotionStudio.Core.Checks.exe");
            var factory = new WindowsMotionHostProcessFactory(harness);
            IMotionHostSession session = factory.Start(request with { Mode = MotionHostMode.Launch, Playback = null });
            try
            {
                MotionHostOutcome outcome = session.HandoffAsync().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                Check("F09 real pipe peer with old protocol reports structured mismatch", outcome == MotionHostOutcome.Failed && session.FailureReason == MotionHostError.UnsupportedProtocol, ref failures);
            }
            finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            HostCompatibility(json, ref failures);
        }
    }

    private static void HostCompatibility(string validStart, ref int failures)
    {
        string? executable = Environment.GetEnvironmentVariable("DMS_F06_HOST_PATH");
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) { Console.WriteLine("SKIP F09 actual host compatibility (DMS_F06_HOST_PATH unset)"); return; }
        foreach (string field in new[] { "schemaVersion", "runtimeVersion" })
        {
            Guid request = Guid.NewGuid(); string name = $"dms-motionhost-{request:N}";
            using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
            foreach (string arg in new[] { "--pipe", name, "--request-id", request.ToString("D") }) start.ArgumentList.Add(arg);
            using Process host = Process.Start(start) ?? throw new InvalidOperationException("Compatibility host did not start.");
            try
            {
                pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                MotionHostFrame? ready = MotionHostFrameCodec.ReadAsync(pipe).WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                if (ready?.Kind != MotionHostMessageKind.Ready) throw new InvalidOperationException("Compatibility host did not send Ready.");
                JsonObject node = JsonNode.Parse(validStart)!.AsObject(); node["requestId"] = request.ToString("D"); node["start"]!["playback"]![field] = 99;
                pipe.Write(RawFrame(node.ToJsonString())); pipe.Flush();
                MotionHostFrame? rejection = MotionHostFrameCodec.ReadAsync(pipe).WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                bool visible = HasVisibleWindow(host.Id);
                Check($"F09 actual host rejects future {field} with precise failure before Started/window", rejection is { Kind: MotionHostMessageKind.Failed, Error: MotionHostError.UnsupportedPlayback } && rejection.RequestId == request && !visible, ref failures);
                Check($"F09 rejected {field} host exits without a display", host.WaitForExit(2000), ref failures);
            }
            finally { if (!host.HasExited) { host.Kill(); host.WaitForExit(1000); } }
        }
    }

    private static bool HasVisibleWindow(int processId)
    {
        bool found = false;
        EnumWindows((window, _) => { GetWindowThreadProcessId(window, out uint owner); if (owner == (uint)processId && IsWindowVisible(window)) found = true; return !found; }, 0);
        return found;
    }
    private delegate bool WindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);

    private static byte[] RawFrame(string json)
    {
        byte[] payload = Bytes(json), frame = new byte[payload.Length + MotionHostFrameCodec.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)payload.Length); payload.CopyTo(frame, MotionHostFrameCodec.HeaderSize);
        return frame;
    }

    internal static string WriteScene(string catalog, string directory, string id = "studio-pulse")
    {
        string scene = Path.Combine(catalog, directory); Directory.CreateDirectory(scene);
        File.WriteAllText(Path.Combine(scene, "scene.json"), ManifestJson.Replace("studio-pulse", id));
        File.WriteAllText(Path.Combine(scene, "scene.presentation.json"), PresentationJson.Replace("studio-pulse", id));
        File.WriteAllText(Path.Combine(scene, "scene.playback.json"), PlaybackJson.Replace("studio-pulse", id));
        return scene;
    }

    internal static void CreateJunction(string alias, string target)
    {
        string script = "New-Item -ItemType Junction -Path '" + alias.Replace("'", "''") + "' -Target '" + target.Replace("'", "''") + "' -ErrorAction Stop | Out-Null";
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(arg);
        using Process creator = Process.Start(start) ?? throw new InvalidOperationException("Junction fixture did not start.");
        if (!creator.WaitForExit(5000)) { creator.Kill(); creator.WaitForExit(1000); throw new TimeoutException("Junction fixture timed out."); }
        if (creator.ExitCode != 0) throw new InvalidOperationException("Junction fixture failed.");
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    internal static void Check(string name, bool passed, ref int failures) { Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}"); if (!passed) failures++; }
}
