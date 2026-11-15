using System.Diagnostics;
using System.Runtime.InteropServices;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.Video;
using Windows.Win32;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.Audio.Endpoints;
using Windows.Win32.System.Com;

if (args.Length != 2) return 2;
string worker = Path.GetFullPath(args[0]), media = Path.GetFullPath(args[1]);
var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out IMMDevice device);
device.Activate(typeof(IAudioSessionManager2).GUID, CLSCTX.CLSCTX_ALL, null, out object managerObject);
var manager = (IAudioSessionManager2)managerObject;
device.Activate(typeof(IAudioMeterInformation).GUID, CLSCTX.CLSCTX_ALL, null, out object meterObject);
var endpointMeter = (IAudioMeterInformation)meterObject;
float baseline = 0;
for (int i = 0; i < 12; i++)
{
    endpointMeter.GetPeakValue(out float value);
    baseline = Math.Max(baseline, value);
    await Task.Delay(65);
}
Console.WriteLine($"endpoint baseline={baseline:F5}");
if (baseline > .001)
{
    Console.WriteLine("SKIP: The default playback endpoint is already active.");
    return 3;
}

int failures = 0;
foreach (bool sound in new[] { false, true })
{
    var factory = new VideoPlaybackSessionFactory(worker);
    await using var playback = factory.Start(Guid.NewGuid(),
        new VideoClip("f21-sound", media, 6000, VideoFit.Complete, sound), SceneDisplayMode.Centered, preview: true);
    if (await playback.Started is not null)
    {
        Console.Error.WriteLine($"FAIL: Cannot start audio test: {playback.FailureReason}");
        return 1;
    }
    uint pid = 0;
    foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(worker)))
    {
        using (process)
        {
            if (!string.Equals(process.MainModule?.FileName, worker, StringComparison.OrdinalIgnoreCase)) continue;
            if (pid != 0) throw new InvalidOperationException("Multiple workers match the test path.");
            pid = (uint)process.Id;
        }
    }
    if (pid == 0) throw new InvalidOperationException("Could not identify the owned worker.");
    int samples = 0;
    float peak = 0, endpointPeak = 0;
    for (int sample = 0; sample < 90; sample++)
    {
        var sessions = manager.GetSessionEnumerator();
        try
        {
            sessions.GetCount(out int count);
            for (int index = 0; index < count; index++)
            {
                sessions.GetSession(index, out IAudioSessionControl control);
                try
                {
                    if (control is not IAudioSessionControl2 session) continue;
                    session.GetProcessId(out uint owner);
                    if (owner != pid || control is not IAudioMeterInformation meter) continue;
                    meter.GetPeakValue(out float value);
                    peak = Math.Max(peak, value);
                    samples++;
                }
                finally { Marshal.ReleaseComObject(control); }
            }
        }
        finally { Marshal.ReleaseComObject(sessions); }
        endpointMeter.GetPeakValue(out float mixed);
        endpointPeak = Math.Max(endpointPeak, mixed);
        await Task.Delay(20);
    }
    await playback.DisposeAsync();
    Console.WriteLine($"sound={sound}; owned PID={pid}; samples={samples}; sessionPeak={peak:F5}; endpointPeak={endpointPeak:F5}");
    if (sound ? samples == 0 || peak < .005 || endpointPeak < .005 : peak > .001 || endpointPeak > .001) failures++;
}
return failures == 0 ? 0 : 1;
