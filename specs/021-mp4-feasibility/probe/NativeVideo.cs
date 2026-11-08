using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.Media.MediaFoundation;

[assembly: SupportedOSPlatform("windows10.0.22000")]

namespace Mp4MotionProbe;

internal sealed class NativeVideo : IDisposable
{
    private readonly PlaybackEvents _events = new();
    private IMFMediaEngine? _engine;
    private IWICBitmap? _surface;
    private byte[]? _pixels;
    private bool _started;

    internal Bitmap? Frame { get; private set; }
    internal double Position => _engine!.GetCurrentTime();
    internal double Duration => _engine!.GetDuration();
    internal bool Ended => _engine!.IsEnded();
    internal bool HasAudio => _engine!.HasAudio();
    internal bool Muted => _engine!.GetMuted();

    internal void Open(string path)
    {
        PInvoke.MFStartup(PInvoke.MF_VERSION, 0).ThrowOnFailure();
        _started = true;
        PInvoke.MFCreateAttributes(out IMFAttributes attributes, 2).ThrowOnFailure();
        IMFMediaEngineClassFactory? factory = null;
        try
        {
            factory = Create<IMFMediaEngineClassFactory>(PInvoke.CLSID_MFMediaEngineClassFactory);
            attributes.SetUnknown(PInvoke.MF_MEDIA_ENGINE_CALLBACK, _events);
            attributes.SetUINT32(PInvoke.MF_MEDIA_ENGINE_VIDEO_OUTPUT_FORMAT, (uint)DXGI_FORMAT.DXGI_FORMAT_B8G8R8X8_UNORM);
            factory.CreateInstance(0, attributes, out IMFMediaEngine engine);
            _engine = engine;
            engine.SetMuted(true);
            nint source = Marshal.StringToBSTR(new Uri(path).AbsoluteUri);
            try { engine.SetSource(new BSTR(source)); }
            finally { Marshal.FreeBSTR(source); }
            engine.Play();
        }
        finally
        {
            if (factory is not null) Marshal.FinalReleaseComObject(factory);
            Marshal.FinalReleaseComObject(attributes);
        }
    }

    internal bool TransferFrame()
    {
        int mediaError = _events.Error;
        if (mediaError != 0) throw new COMException("The local clip could not be played.", mediaError);
        var engine = _engine!;
        if (!_events.FirstFrameReady) return false;
        HRESULT tick = engine.OnVideoStreamTick(out _);
        if (tick.Value != 0)
        {
            tick.ThrowOnFailure();
            return false;
        }
        if (_surface is null) CreateSurface(engine);
        Bitmap frame = Frame!;
        var bounds = new Rectangle(0, 0, frame.Width, frame.Height);
        engine.TransferVideoFrame(_surface!, null, new RECT(bounds), null);
        _surface!.CopyPixels(null, (uint)(frame.Width * 4), _pixels!);
        BitmapData bitmap = frame.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            for (int y = 0; y < frame.Height; y++)
                Marshal.Copy(_pixels!, y * frame.Width * 4, bitmap.Scan0 + y * bitmap.Stride, frame.Width * 4);
        }
        finally { frame.UnlockBits(bitmap); }
        return true;
    }

    private void CreateSurface(IMFMediaEngine engine)
    {
        engine.GetNativeVideoSize(out uint width, out uint height);
        if (width is 0 or > 1920 || height is 0 or > 1920 || width * height > 1920 * 1080)
            throw new InvalidDataException("Video dimensions exceed the probe budget.");
        var imaging = Create<IWICImagingFactory>(PInvoke.CLSID_WICImagingFactory);
        try
        {
            imaging.CreateBitmap(width, height, PInvoke.GUID_WICPixelFormat32bppBGR,
                WICBitmapCreateCacheOption.WICBitmapCacheOnLoad, out IWICBitmap surface);
            _surface = surface;
        }
        finally { Marshal.FinalReleaseComObject(imaging); }
        Frame = new Bitmap((int)width, (int)height, PixelFormat.Format32bppRgb);
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
            Frame?.Dispose();
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
        switch ((MF_MEDIA_ENGINE_EVENT)mediaEvent)
        {
            case MF_MEDIA_ENGINE_EVENT.MF_MEDIA_ENGINE_EVENT_FIRSTFRAMEREADY:
                Volatile.Write(ref _firstFrameReady, 1);
                break;
            case MF_MEDIA_ENGINE_EVENT.MF_MEDIA_ENGINE_EVENT_ERROR:
                Volatile.Write(ref _error, param2 == 0 ? unchecked((int)0x80004005) : unchecked((int)param2));
                break;
        }
    }
}
