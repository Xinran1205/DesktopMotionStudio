using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.Media.MediaFoundation;

[assembly: SupportedOSPlatform("windows10.0.22000")]

namespace DesktopMotionStudio.VideoHost;

internal sealed class NativeVideo : IDisposable
{
    private readonly PlaybackEvents _events = new();
    private IMFMediaEngine? _engine;
    private IWICBitmap? _surface;
    private SilentVideo? _silent;
    private byte[]? _pixels;
    private bool _started;
    private bool _sound;
    private Bitmap? _frame;

    internal Bitmap? Frame => _silent?.Frame ?? _frame;
    internal double Duration => _silent?.Duration ?? _engine!.GetDuration();
    internal double Position => _silent?.Position ?? _engine!.GetCurrentTime();
    internal bool ReachedEnd => _silent?.ReachedEnd ?? _engine!.IsEnded();

    internal void Open(string path, bool sound)
    {
        PInvoke.MFStartup(PInvoke.MF_VERSION, 0).ThrowOnFailure();
        _started = true;
        bool hasAudio = VideoMediaProfile.HasAudio(path);
        if (!sound && hasAudio)
        {
            _silent = new SilentVideo();
            _silent.Open(path);
            return;
        }
        _sound = sound && hasAudio;
        PInvoke.MFCreateAttributes(out IMFAttributes attributes, 2).ThrowOnFailure();
        IMFMediaEngineClassFactory? factory = null;
        try
        {
            factory = Create<IMFMediaEngineClassFactory>(PInvoke.CLSID_MFMediaEngineClassFactory);
            attributes.SetUnknown(PInvoke.MF_MEDIA_ENGINE_CALLBACK, _events);
            attributes.SetUINT32(PInvoke.MF_MEDIA_ENGINE_VIDEO_OUTPUT_FORMAT, (uint)DXGI_FORMAT.DXGI_FORMAT_B8G8R8X8_UNORM);
            const MF_MEDIA_ENGINE_CREATEFLAGS flags = MF_MEDIA_ENGINE_CREATEFLAGS.MF_MEDIA_ENGINE_DISABLE_LOCAL_PLUGINS;
            factory.CreateInstance((uint)flags, attributes, out IMFMediaEngine engine);
            _engine = engine;
            engine.SetMuted(true);
            engine.SetPreload(MF_MEDIA_ENGINE_PRELOAD.MF_MEDIA_ENGINE_PRELOAD_AUTOMATIC);
            nint source = Marshal.StringToBSTR(new Uri(path).AbsoluteUri);
            try { engine.SetSource(new BSTR(source)); }
            finally { Marshal.FreeBSTR(source); }
        }
        finally
        {
            if (factory is not null) Marshal.FinalReleaseComObject(factory);
            Marshal.FinalReleaseComObject(attributes);
        }
    }

    internal void Play()
    {
        if (_engine is not { } engine) return;
        engine.SetMuted(!_sound);
        engine.Play();
    }

    internal bool TransferFrame(double elapsed = 0)
    {
        if (_silent is { } silent) return silent.TransferFrame(elapsed);
        int mediaError = _events.Error;
        if (mediaError != 0) throw new COMException("The local video could not be played.", mediaError);
        var engine = _engine!;
        if (!_events.FirstFrameReady || engine.IsEnded()) return false;
        if (_surface is null) CreateSurface(engine);
        else
        {
            HRESULT tick = engine.OnVideoStreamTick(out _);
            if (tick.Value != 0) { tick.ThrowOnFailure(); return false; }
        }
        Bitmap frame = Frame!;
        var bounds = new Rectangle(0, 0, frame.Width, frame.Height);
        engine.TransferVideoFrame(_surface!, null, new RECT(bounds), null);
        _surface!.CopyPixels(null, (uint)(frame.Width * 4), _pixels!);
        BitmapData bitmap = frame.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            int rowBytes = frame.Width * 4;
            if (bitmap.Stride == rowBytes) Marshal.Copy(_pixels!, 0, bitmap.Scan0, _pixels!.Length);
            else
                for (int y = 0; y < frame.Height; y++)
                    Marshal.Copy(_pixels!, y * rowBytes, bitmap.Scan0 + y * bitmap.Stride, rowBytes);
        }
        finally { frame.UnlockBits(bitmap); }
        return true;
    }

    private void CreateSurface(IMFMediaEngine engine)
    {
        engine.GetNativeVideoSize(out uint width, out uint height);
        if (!VideoMediaProfile.ValidSize(width, height)) throw new InvalidDataException("Unsupported video frame size.");
        var imaging = Create<IWICImagingFactory>(PInvoke.CLSID_WICImagingFactory);
        try
        {
            imaging.CreateBitmap(width, height, PInvoke.GUID_WICPixelFormat32bppBGR,
                WICBitmapCreateCacheOption.WICBitmapCacheOnLoad, out IWICBitmap surface);
            _surface = surface;
        }
        finally { Marshal.FinalReleaseComObject(imaging); }
        _frame = new Bitmap((int)width, (int)height, PixelFormat.Format32bppRgb);
        _pixels = new byte[checked((int)(width * height * 4))];
    }

    private static T Create<T>(Guid clsid) where T : class =>
        (T)(Activator.CreateInstance(Type.GetTypeFromCLSID(clsid, throwOnError: true)!) ??
            throw new InvalidOperationException("Windows media component could not be created."));

    public void Dispose()
    {
        try { _engine?.Shutdown(); }
        finally
        {
            _silent?.Dispose();
            _frame?.Dispose();
            if (_surface is not null) Marshal.FinalReleaseComObject(_surface);
            if (_engine is not null) Marshal.FinalReleaseComObject(_engine);
            if (_started) PInvoke.MFShutdown().ThrowOnFailure();
        }
    }
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class PlaybackEvents : IMFMediaEngineNotify
{
    private int _firstFrameReady;
    private int _error;
    internal bool FirstFrameReady => Volatile.Read(ref _firstFrameReady) != 0;
    internal int Error => Volatile.Read(ref _error);

    public void EventNotify(uint mediaEvent, nuint param1, uint param2)
    {
        if (mediaEvent == (uint)MF_MEDIA_ENGINE_EVENT.MF_MEDIA_ENGINE_EVENT_FIRSTFRAMEREADY)
            Volatile.Write(ref _firstFrameReady, 1);
        else if (mediaEvent == (uint)MF_MEDIA_ENGINE_EVENT.MF_MEDIA_ENGINE_EVENT_ERROR)
            Volatile.Write(ref _error, param2 == 0 ? unchecked((int)0x80004005) : unchecked((int)param2));
    }
}
