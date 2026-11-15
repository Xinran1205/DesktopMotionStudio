using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.MediaFoundation;
using Windows.Win32.System.Variant;

namespace DesktopMotionStudio.VideoHost;

internal sealed class SilentVideo : IDisposable
{
    private const uint VideoStream = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_FIRST_VIDEO_STREAM);
    private const uint AllStreams = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_ALL_STREAMS);
    private const uint MediaSource = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_MEDIASOURCE);
    private IMFSourceReader? _reader;
    private IMFSample? _next;
    private long? _origin;
    private long _nextTime;
    private long _lastTime, _lastDuration;
    private bool _ended;
    private int _stride;
    private int _storageHeight;
    private int _left, _top;

    internal Bitmap? Frame { get; private set; }
    internal double Duration { get; private set; }
    internal double Position { get; private set; }
    internal bool ReachedEnd => _ended && _next is null && _origin is not null;

    internal void Open(string path)
    {
        PInvoke.MFCreateAttributes(out IMFAttributes attributes, 1).ThrowOnFailure();
        try
        {
            attributes.SetUINT32(PInvoke.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);
            PInvoke.MFCreateSourceReaderFromURL(path, attributes, out IMFSourceReader reader).ThrowOnFailure();
            _reader = reader;
        }
        finally { Marshal.FinalReleaseComObject(attributes); }

        _reader.SetStreamSelection(AllStreams, false);
        _reader.SetStreamSelection(VideoStream, true);
        _reader.GetPresentationAttribute(MediaSource, PInvoke.MF_PD_DURATION, out var duration);
        try
        {
            if (duration.vt != VARENUM.VT_UI8 || duration.uhVal > long.MaxValue)
                throw new InvalidDataException("Unsupported MP4 duration.");
            Duration = (double)duration.uhVal / TimeSpan.TicksPerSecond;
        }
        finally { PInvoke.PropVariantClear(ref duration).ThrowOnFailure(); }

        PInvoke.MFCreateMediaType(out IMFMediaType requested).ThrowOnFailure();
        try
        {
            requested.SetGUID(PInvoke.MF_MT_MAJOR_TYPE, PInvoke.MFMediaType_Video);
            requested.SetGUID(PInvoke.MF_MT_SUBTYPE, PInvoke.MFVideoFormat_RGB32);
            _reader.SetCurrentMediaType(VideoStream, requested);
        }
        finally { Marshal.FinalReleaseComObject(requested); }
    }

    private void InitializeFrame()
    {
        _reader!.GetCurrentMediaType(VideoStream, out IMFMediaType format);
        try
        {
            format.GetUINT64(PInvoke.MF_MT_FRAME_SIZE, out ulong size);
            uint storageWidth = (uint)(size >> 32), storageHeight = (uint)size;
            if (storageWidth is 0 or > 1952 || storageHeight is 0 or > 1952)
                throw new InvalidDataException("Unsupported MP4 frame storage.");
            _storageHeight = (int)storageHeight;
            uint width = storageWidth, height = storageHeight;
            if ((ReadAperture(format, PInvoke.MF_MT_MINIMUM_DISPLAY_APERTURE) ??
                ReadAperture(format, PInvoke.MF_MT_GEOMETRIC_APERTURE)) is { } aperture)
            {
                if (aperture.OffsetX.fract != 0 || aperture.OffsetY.fract != 0)
                    throw new InvalidDataException("Unsupported MP4 display aperture.");
                _left = aperture.OffsetX.value;
                _top = aperture.OffsetY.value;
                width = (uint)aperture.Area.cx;
                height = (uint)aperture.Area.cy;
            }
            if (!VideoMediaProfile.ValidSize(width, height) || _left < 0 || _top < 0 ||
                _left + width > storageWidth || _top + height > storageHeight)
                throw new InvalidDataException("Unsupported MP4 frame size.");
            try { format.GetUINT32(PInvoke.MF_MT_DEFAULT_STRIDE, out uint stride); _stride = unchecked((int)stride); }
            catch (COMException error) when (error.HResult == HRESULT.MF_E_ATTRIBUTENOTFOUND)
            { _stride = -(int)storageWidth * 4; }
            if (Math.Abs((long)_stride) < storageWidth * 4 || Math.Abs((long)_stride) > 16_384)
                throw new InvalidDataException("Unsupported MP4 frame stride.");
            Frame = new Bitmap((int)width, (int)height, PixelFormat.Format32bppRgb);
        }
        finally { Marshal.FinalReleaseComObject(format); }
    }

    private static MFVideoArea? ReadAperture(IMFMediaType format, Guid key)
    {
        byte[] data = new byte[Marshal.SizeOf<MFVideoArea>()];
        uint bytes = 0;
        try { format.GetBlob(key, data, ref bytes); }
        catch (COMException error) when (error.HResult == HRESULT.MF_E_ATTRIBUTENOTFOUND) { return null; }
        if (bytes != data.Length) throw new InvalidDataException("Unsupported MP4 display aperture.");
        return MemoryMarshal.Read<MFVideoArea>(data);
    }

    internal bool TransferFrame(double elapsed)
    {
        bool updated = false;
        long due = (long)(elapsed * TimeSpan.TicksPerSecond);
        for (int frame = 0; frame < 4; frame++)
        {
            if (_next is null)
            {
                if (_ended) break;
                _reader!.ReadSample(VideoStream, 0, out _, out uint flags, out long timestamp, out IMFSample sample);
                _next = sample;
                const uint changed = (uint)(MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_NATIVEMEDIATYPECHANGED |
                    MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED);
                if ((flags & (uint)MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ERROR) != 0 ||
                    (_origin is not null && (flags & changed) != 0))
                    throw new InvalidDataException("The MP4 video stream changed during playback.");
                _ended = (flags & (uint)MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM) != 0;
                if (_next is null) continue;
                if (Frame is null) InitializeFrame();
                _origin ??= timestamp;
                _nextTime = checked(timestamp - _origin.Value);
            }
            if (_nextTime > due) break;
            CopyFrame(_next);
            long length;
            try { _next.GetSampleDuration(out length); }
            catch (COMException error) when (error.HResult == HRESULT.MF_E_NO_SAMPLE_DURATION)
            { length = Math.Max(_lastDuration, _nextTime - _lastTime); }
            Marshal.FinalReleaseComObject(_next);
            _next = null;
            _lastTime = _nextTime;
            _lastDuration = Math.Clamp(length, 0, TimeSpan.TicksPerSecond);
            Position = (double)checked(_lastTime + _lastDuration) / TimeSpan.TicksPerSecond;
            updated = true;
            if (due == 0) break;
        }
        if (_ended && _origin is null) throw new InvalidDataException("The MP4 has no playable video segment.");
        return updated;
    }

    private unsafe void CopyFrame(IMFSample sample)
    {
        sample.ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
        try
        {
            buffer.Lock(out byte* pixels, out _, out uint length);
            try
            {
                Bitmap image = Frame!;
                int rowBytes = image.Width * 4;
                int sourcePitch = Math.Abs(_stride);
                if (pixels == null || length < (uint)(sourcePitch * _storageHeight))
                    throw new InvalidDataException("The decoded frame is truncated.");
                byte* top = _stride < 0 ? pixels + sourcePitch * (_storageHeight - 1) : pixels;
                top += _top * _stride + _left * 4;
                var bounds = new Rectangle(0, 0, image.Width, image.Height);
                BitmapData output = image.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                try
                {
                    for (int row = 0; row < image.Height; row++)
                        Buffer.MemoryCopy(top + row * _stride, (byte*)output.Scan0 + row * output.Stride, rowBytes, rowBytes);
                }
                finally { image.UnlockBits(output); }
            }
            finally { buffer.Unlock(); }
        }
        finally { Marshal.FinalReleaseComObject(buffer); }
    }

    public void Dispose()
    {
        Frame?.Dispose();
        if (_next is not null) Marshal.FinalReleaseComObject(_next);
        if (_reader is not null) Marshal.FinalReleaseComObject(_reader);
    }
}
